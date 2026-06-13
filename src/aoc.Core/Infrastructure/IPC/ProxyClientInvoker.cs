using System.Buffers;
using System.IO.Pipes;
using System.Security.Principal;

namespace aoc.Infrastructure.IPC;

/// <summary>
/// IAocInvoker implementation that delegates SDK calls to the 32-bit ZeasnProxy
/// process via named pipe JSON-RPC.
///
/// Thread safety: All IPC operations (buffer write + pipe write + response read)
/// are protected by SemaphoreSlim to support concurrent callers.
/// A fresh ArrayBufferWriter + Utf8JsonWriter is created per call to avoid
/// state corruption from pooled-object reuse.
/// SendRequest is synchronous to match IAocInvoker's synchronous interface.
/// ConnectAsync provides async connection.
///
/// Security:
///   - Server identity validated on connect via pipe ACL owner check.
///   - PipeOptions.CurrentUserOnly restricts pipe access to the current user.
///   - On IPC I/O failures, the pipe is disconnected and a reconnection is
///     attempted on the next call.
///
/// Performance:
///   - Caches TryInitialize success to skip redundant IPC round-trips.
///   - Writes JSON bytes directly to the pipe base stream (no intermediate string).
///   - Reads response bytes via chunked Stream.Read() (not byte-by-byte) into a
///     growing MemoryStream, then parses directly from UTF-8.
///   - Single WriteParams helper avoids code duplication across SendRequest paths.
/// </summary>
public sealed class ProxyClientInvoker : IAocInvoker, IAsyncDisposable
{
    private readonly string _pipeName;
    private NamedPipeClientStream? _pipe;
    private Stream? _baseStream;
    private int _requestId;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private bool _disposed;

    // ── Reconnection ──────────────────────────────────────────────
    private readonly TimeSpan _connectTimeout;

    // ── Pooled JSON writer ─────────────────────────────────────────
    private ArrayBufferWriter<byte>? _buffer;
    private Utf8JsonWriter? _jsonWriter;

    // ── Reusable read buffers (growing, no fixed cap) ──────────────
    private readonly MemoryStream _readStream = new(4096);
    private readonly byte[] _readChunk = new byte[1024];

    // ── Sentinal empty JSON object for error paths ─────────────────
    private static readonly JsonElement s_emptyObject = JsonDocument.Parse("{}").RootElement.Clone();

    // ── Init state cache ────────────────────────────────────────────
    private bool _initialized;

    public string? LastDiagnostic { get; private set; }

    public ProxyClientInvoker(string pipeName = "ZeasnProxy", TimeSpan? connectTimeout = null)
    {
        _pipeName = pipeName;
        _connectTimeout = connectTimeout ?? TimeSpan.FromSeconds(5);
    }

    public async Task ConnectAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        Debug.WriteLine($"[IPC] Connecting to pipe '{_pipeName}' (timeout: {timeout.TotalSeconds}s)...");
        _pipe = new NamedPipeClientStream(
            ".",
            _pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

        await _pipe.ConnectAsync(timeout, ct);
        _baseStream = _pipe;

        // ── Validate server identity (Windows only) ──
        if (OperatingSystem.IsWindows())
            ValidateServerIdentity();

        Debug.WriteLine($"[IPC] Connected to pipe '{_pipeName}'.");
    }

    /// <summary>
    /// Validates that the named pipe server is owned by the current user.
    /// This prevents a malicious actor from stopping the trusted proxy and
    /// running their own to intercept monitor commands.
    /// </summary>
    /// <exception cref="UnauthorizedAccessException">Thrown if the server owner
    /// does not match the current user.</exception>
#pragma warning disable CA1416
    private void ValidateServerIdentity()
    {
        if (_pipe is null)
            throw new InvalidOperationException("Cannot validate server identity before connecting.");

        try
        {
            var pipeSecurity = _pipe.GetAccessControl();
            if (pipeSecurity.GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier remoteOwner)
            {
                Debug.WriteLine("[IPC] Server identity: no owner SID found (allowing).");
                return;
            }

            if (WindowsIdentity.GetCurrent().User is not SecurityIdentifier currentUser)
            {
                Debug.WriteLine("[IPC] Server identity: no current user SID (allowing).");
                return;
            }

            if (!remoteOwner.Equals(currentUser))
            {
                var msg = $"Server identity mismatch: owner={remoteOwner.Value}, current={currentUser.Value}";
                Debug.WriteLine($"[IPC] !! {msg}");
                LastDiagnostic = msg;
                throw new UnauthorizedAccessException(
                    "Named pipe server is not owned by the current user. Connection rejected.");
            }

            Debug.WriteLine("[IPC] Server identity validated (owner matches current user).");
        }
        catch (UnauthorizedAccessException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Non-critical: validation is best-effort on some systems.
            Debug.WriteLine($"[IPC] Server identity validation skipped: {ex.Message}");
        }
    }
#pragma warning restore CA1416

    /// <summary>
    /// Returns true if the pipe connection appears healthy by checking
    /// <see cref="PipeStream.IsConnected"/> and <see cref="PipeStream.CanWrite"/>.
    /// </summary>
    public bool IsConnected =>
        _pipe is not null && !_disposed &&
        _pipe.IsConnected && _baseStream?.CanWrite == true;

    /// <summary>
    /// Attempts to reconnect the named pipe. Returns true on success.
    /// Resets the initialization cache so a new TryInitialize will be sent.
    /// </summary>
    public async Task<bool> TryReconnectAsync()
    {
        if (_disposed) return false;

        Debug.WriteLine("[IPC] Attempting reconnection...");
        try
        {
            // Dispose old pipe
            if (_baseStream is not null)
                await _baseStream.DisposeAsync();

            _pipe = null;
            _baseStream = null;
            _initialized = false;

            // Reconnect
            await ConnectAsync(_connectTimeout);
            Debug.WriteLine("[IPC] Reconnection succeeded.");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[IPC] Reconnection failed: {ex.Message}");
            LastDiagnostic = $"Reconnection failed: {ex.Message}";
            return false;
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  Sync public methods (IAocInvoker interface) — CLI path
    // ════════════════════════════════════════════════════════════════

    public bool TryInitialize(out string? diagnostic)
    {
        // Fast path: already successfully initialized in this session
        if (_initialized)
        {
            Debug.WriteLine("[IPC] TryInitialize: already initialized (fast path).");
            diagnostic = null;
            return true;
        }

        Debug.WriteLine("[IPC] TryInitialize: sending request...");
        var response = SendRequestCoreAsync("TryInitialize", null, null)
            .GetAwaiter().GetResult();

        if (response.TryGetProperty("success", out var s) && s.GetBoolean())
        {
            _initialized = true;
            diagnostic = null;
            Debug.WriteLine("[IPC] TryInitialize: succeeded.");
            return true;
        }
        diagnostic = response.TryGetProperty("diagnostic", out var d) ? d.GetString() : "Proxy initialization failed";
        LastDiagnostic = diagnostic;
        Debug.WriteLine($"[IPC] TryInitialize: failed. diagnostic={diagnostic}");
        return false;
    }

    public object? Call(string method, params object?[] args)
    {
        Debug.WriteLine($"[IPC] Call: method={method}, args=[{string.Join(", ", args ?? Array.Empty<object>())}]");
        var response = SendRequestCoreAsync("Call", method, args).GetAwaiter().GetResult();
        Debug.WriteLine($"[IPC] Call response: kind={response.ValueKind}");
        return response.ValueKind == JsonValueKind.Null ? null : response;
    }

    public bool Ok(string method, params object?[] args)
    {
        Debug.WriteLine($"[IPC] Ok: method={method}, args=[{string.Join(", ", args ?? Array.Empty<object>())}]");
        var response = SendRequestCoreAsync("Ok", method, args).GetAwaiter().GetResult();
        var success = response.TryGetProperty("success", out var s) && s.GetBoolean();
        Debug.WriteLine($"[IPC] Ok response: success={success}");
        if (success)
            return true;

        LastDiagnostic = response.TryGetProperty("diagnostic", out var d) ? d.GetString() : null;
        return false;
    }

    // ════════════════════════════════════════════════════════════════
    //  Async public methods — UI path (avoids Task.Run / blocked threads)
    // ════════════════════════════════════════════════════════════════

    public async Task<(bool Success, string? Diagnostic)> TryInitializeAsync()
    {
        if (_initialized)
        {
            Debug.WriteLine("[IPC] TryInitializeAsync: already initialized (fast path).");
            return (true, null);
        }

        Debug.WriteLine("[IPC] TryInitializeAsync: sending request...");
        var response = await SendRequestCoreAsync("TryInitialize", null, null).ConfigureAwait(false);

        if (response.TryGetProperty("success", out var s) && s.GetBoolean())
        {
            _initialized = true;
            Debug.WriteLine("[IPC] TryInitializeAsync: succeeded.");
            return (true, null);
        }
        var diag = response.TryGetProperty("diagnostic", out var d) ? d.GetString() : "Proxy initialization failed";
        LastDiagnostic = diag;
        Debug.WriteLine($"[IPC] TryInitializeAsync: failed. diagnostic={diag}");
        return (false, diag);
    }

    public async Task<object?> CallAsync(string method, params object?[] args)
    {
        Debug.WriteLine($"[IPC] CallAsync: method={method}");
        var response = await SendRequestCoreAsync("Call", method, args).ConfigureAwait(false);
        return response.ValueKind == JsonValueKind.Null ? null : response;
    }

    public async Task<bool> OkAsync(string method, params object?[] args)
    {
        Debug.WriteLine($"[IPC] OkAsync: method={method}");
        var response = await SendRequestCoreAsync("Ok", method, args).ConfigureAwait(false);
        var success = response.TryGetProperty("success", out var s) && s.GetBoolean();
        if (success) return true;

        LastDiagnostic = response.TryGetProperty("diagnostic", out var d) ? d.GetString() : null;
        return false;
    }

    /// <summary>
    /// Sends a Shutdown JSON-RPC request to the ZeasnProxy process, causing it to
    /// exit immediately. After this call, the proxy pipe server terminates and no
    /// further requests can be made through this connection.
    ///
    /// Thread safety: This method bypasses _sendLock intentionally — it is only
    /// called during shutdown, when concurrent callers should already have completed
    /// or will fail harmlessly from the disposed pipe.
    /// </summary>
    public void SendShutdown()
    {
        if (_disposed || _baseStream is null) return;

        Debug.WriteLine("[IPC] Sending Shutdown...");
        try
        {
            // Write directly (no lock) — we are shutting down.
            var request = """{"method":"Shutdown","params":[],"id":0}""" + "\n";
            var bytes = System.Text.Encoding.UTF8.GetBytes(request);
            _baseStream.Write(bytes);
            _baseStream.Flush();

            // Read response: {"jsonrpc":"2.0","result":"shutting down","id":0}
            var responseBytes = ReadLine();
            Debug.WriteLine($"[IPC] Shutdown response: {System.Text.Encoding.UTF8.GetString(responseBytes)}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[IPC] Shutdown error (non-fatal): {ex.Message}");
        }
    }

    /// <summary>
    /// Core JSON-RPC request builder. Delegates to the async version for
    /// single implementation. Sync method used by the CLI path.
    ///
    /// Error recovery: On IOException, attempts a single reconnection and retry.
    /// </summary>
    private JsonElement SendRequestCore(string rpcMethod, string? sdkMethod, object?[]? args)
        => SendRequestCoreAsync(rpcMethod, sdkMethod, args).GetAwaiter().GetResult();

    /// <summary>
    /// Async core: builds, sends, and receives a JSON-RPC request.
    /// Thread safety: entire operation under _sendLock (async-compatible).
    /// Error recovery: IOException triggers reconnect + single retry.
    /// </summary>
    private async Task<JsonElement> SendRequestCoreAsync(string rpcMethod, string? sdkMethod, object?[]? args)
    {
        if (_disposed)
        {
            LastDiagnostic = "Proxy invoker is disposed.";
            return s_emptyObject;
        }

        if (_baseStream is null)
        {
            LastDiagnostic = "Not connected to proxy. Call ConnectAsync first.";
            return s_emptyObject;
        }

        try
        {
            return await SendRequestInnerAsync(rpcMethod, sdkMethod, args).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            Debug.WriteLine($"[IPC] !! IOException: {ex.Message}");

            LastDiagnostic = $"IPC connection error: {ex.Message}";
            var reconnectOk = await TryReconnectAsync().ConfigureAwait(false);
            if (reconnectOk)
            {
                Debug.WriteLine("[IPC] Retrying after reconnection...");
                try
                {
                    return await SendRequestInnerAsync(rpcMethod, sdkMethod, args).ConfigureAwait(false);
                }
                catch (Exception inner)
                {
                    Debug.WriteLine($"[IPC] !! Retry also failed: {inner.Message}");
                    LastDiagnostic = $"IPC retry failed: {inner.Message}";
                    return s_emptyObject;
                }
            }

            return s_emptyObject;
        }
        catch (JsonException ex)
        {
            Debug.WriteLine($"[IPC] !! JsonException: {ex.Message}");
            LastDiagnostic = $"IPC protocol error: {ex.Message}";
            return s_emptyObject;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[IPC] !! Unexpected: {ex.GetType().Name}: {ex.Message}");
            LastDiagnostic = $"IPC unexpected error: {ex.Message}";
            return s_emptyObject;
        }
    }

    /// <summary>
    /// Inner async send/receive loop. Acquires _sendLock, serializes JSON,
    /// writes to pipe asynchronously, reads response asynchronously.
    /// Must not be called directly — use SendRequestCoreAsync for error recovery.
    /// </summary>
    private async Task<JsonElement> SendRequestInnerAsync(string rpcMethod, string? sdkMethod, object?[]? args)
    {
        await _sendLock.WaitAsync().ConfigureAwait(false);
        try
        {
            // ── Build JSON request into pooled buffer ──
            InitWriter();
            var id = Interlocked.Increment(ref _requestId);

            _jsonWriter!.WriteStartObject();
            _jsonWriter.WriteString("method", rpcMethod);
            _jsonWriter.WriteStartArray("params");

            if (sdkMethod is not null)
                _jsonWriter.WriteStringValue(sdkMethod);

            if (args is not null)
                WriteParams(args);

            _jsonWriter.WriteEndArray();
            _jsonWriter.WriteNumber("id", id);
            _jsonWriter.WriteEndObject();
            _jsonWriter.Flush();

            // ── Write to pipe ──
            var span = _buffer!.WrittenSpan;
#if DEBUG
            var requestText = System.Text.Encoding.UTF8.GetString(span);
            Debug.WriteLine($"[IPC] >> {requestText}");
#endif
            await _baseStream!.WriteAsync(_buffer.WrittenMemory).ConfigureAwait(false);
            _baseStream!.WriteByte((byte)'\n');
            await _baseStream.FlushAsync().ConfigureAwait(false);

            // ── Read response ──
            var responseBytes = await ReadLineAsync().ConfigureAwait(false);
            if (responseBytes.Length == 0)
            {
                Debug.WriteLine("[IPC] << (empty — proxy disconnected)");
                LastDiagnostic = "Proxy disconnected";
                return s_emptyObject;
            }

#if DEBUG
            var responseText = System.Text.Encoding.UTF8.GetString(responseBytes);
            Debug.WriteLine($"[IPC] << {responseText}");
#endif

            using var doc = JsonDocument.Parse(responseBytes);
            var root = doc.RootElement;

            if (root.TryGetProperty("error", out var error))
            {
                LastDiagnostic = $"IPC error: {error.GetProperty("message").GetString()}";
                return s_emptyObject;
            }

            return root.GetProperty("result").Clone();
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <summary>
    /// Writes a sequence of values as JSON array elements, using the most
    /// compact representation for each known type.
    /// </summary>
    private void WriteParams(object?[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var p = args[i];
            if (p is null) _jsonWriter!.WriteNullValue();
            else if (p is string s) _jsonWriter!.WriteStringValue(s);
            else if (p is int iv) _jsonWriter!.WriteNumberValue(iv);
            else if (p is bool b) _jsonWriter!.WriteBooleanValue(b);
            else if (p is long l) _jsonWriter!.WriteNumberValue(l);
            else _jsonWriter!.WriteStringValue(p.ToString());
        }
    }

    /// <summary>
    /// Creates a fresh JSON writer and buffer each time.
    /// No pooling — avoids state corruption from ArrayBufferWriter.Clear()
    /// + Utf8JsonWriter.Reset(). Allocation is negligible compared to IPC I/O.
    /// NOTE: must be called UNDER _sendLock.
    /// </summary>
    private void InitWriter()
    {
        _jsonWriter?.Dispose();
        _buffer = new ArrayBufferWriter<byte>(256);
        _jsonWriter = new Utf8JsonWriter(_buffer);
    }

    /// <summary>
    /// Sync wrapper for ReadLineAsync. Used by SendShutdown (shutdown-only path).
    /// </summary>
    private byte[] ReadLine()
        => ReadLineAsync().GetAwaiter().GetResult();

    /// <summary>
    /// Reads a newline-terminated UTF-8 line from the pipe base stream
    /// into a reusable growing buffer using chunked Stream.ReadAsync() calls.
    /// Returns the byte array (excluding the newline), or empty on EOF.
    /// </summary>
    private async Task<byte[]> ReadLineAsync()
    {
        _readStream.SetLength(0);
        _readStream.Position = 0;

        while (true)
        {
            var bytesRead = await _baseStream!.ReadAsync(_readChunk, 0, _readChunk.Length)
                .ConfigureAwait(false);
            if (bytesRead == 0) break; // EOF

            for (var i = 0; i < bytesRead; i++)
            {
                if (_readChunk[i] == (byte)'\n')
                {
                    // Line terminator found — write everything before it
                    if (i > 0)
                        _readStream.Write(_readChunk, 0, i);
                    return _readStream.GetBuffer().AsSpan(0, (int)_readStream.Length).ToArray();
                }
            }

            // No newline in this chunk — accumulate and continue
            _readStream.Write(_readChunk, 0, bytesRead);
        }

        return [];
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _sendLock.Dispose();
        _jsonWriter?.Dispose();
        _buffer?.Clear();
        _readStream.Dispose();
        if (_baseStream is not null)
            await _baseStream.DisposeAsync();
    }
}
