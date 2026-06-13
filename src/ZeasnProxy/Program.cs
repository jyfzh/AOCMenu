const int IdleTimeoutSec = 30;

var binDir = Path.Combine(AppContext.BaseDirectory, "sdk");
if (!Directory.Exists(binDir))
{
    Console.Error.WriteLine(
        $"Proxy startup failed: SDK directory not found at '{binDir}'. " +
        $"Ensure Zeasn SDK DLLs are present in the ZeasnProxy project's sdk/ directory.");
    Environment.Exit(1);
}
var invoker = AocSdkInvoker.CreateDefault(binDir);

// Signal to clients that a proxy instance is ready.
// ProxyHost uses this named event to avoid starting a duplicate process.
using var _runningEvent = new EventWaitHandle(
    initialState: true,
    mode: EventResetMode.ManualReset,
    name: "ZeasnProxy_Running");

Console.Error.WriteLine($"Proxy started, pipe=ZeasnProxy, pid={Environment.ProcessId}");

var shutdownCts = new CancellationTokenSource();
var sessionTracker = new SessionTracker(IdleTimeoutSec);

// ── Accept loop: multi-instance named pipe server ──────────────────
// Each iteration creates a new NamedPipeServerStream instance. The OS
// manages the instance pool (up to MaxAllowedServerInstances).
// When sessions are active, WaitForConnectionAsync has no idle timeout.
// When idle, the SessionTracker includes an idle-timeout token that fires
// after IdleTimeoutSec, causing the accept loop to exit gracefully.
while (!shutdownCts.IsCancellationRequested)
{
    var server = new NamedPipeServerStream(
        "ZeasnProxy",
        PipeDirection.InOut,
        NamedPipeServerStream.MaxAllowedServerInstances,
        PipeTransmissionMode.Byte,
        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    try
    {
        var waitCts = sessionTracker.CreateWaitCts(shutdownCts.Token);
        try
        {
            await server.WaitForConnectionAsync(waitCts.Token);
        }
        finally
        {
            waitCts.Dispose();
            sessionTracker.ClearWaitCts();
        }
    }
    catch (OperationCanceledException)
    {
        server.Dispose();

        if (shutdownCts.IsCancellationRequested)
            break; // Shutdown RPC was called

        if (sessionTracker.IsIdle)
            break; // All sessions disconnected and idle timeout fired — exit

        // A session reconnected during the cancellation — retry accept
        continue;
    }

    // New client connected — dispatch to background handler task
    // The session handler takes ownership of pipe disposal.
    var sessionId = sessionTracker.RegisterSession();
    _ = Task.Run(() => HandleSessionAsync(sessionId, server, invoker, sessionTracker, shutdownCts.Token));
}

// Cleanup: dispose session tracker (cancels idle timeout) and signal any
// remaining sessions. Process exits when Main returns — background tasks
// are terminated by the runtime.
sessionTracker.Dispose();

// ── Per-session handler ────────────────────────────────────────────
// Processes JSON-RPC requests for a single connected client.
// Runs on a thread-pool task. On disconnect or shutdown, unregisters
// the session and disposes the pipe.
async Task HandleSessionAsync(
    Guid sessionId,
    NamedPipeServerStream pipe,
    AocSdkInvoker invoker,
    SessionTracker tracker,
    CancellationToken ct)
{
    try
    {
        using var reader = new StreamReader(pipe);
        using var writer = new StreamWriter(pipe) { AutoFlush = true };

        string? line;
        while (!ct.IsCancellationRequested && (line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                var method = root.GetProperty("method").GetString()!;
                var id = root.TryGetProperty("id", out var idEl) ? idEl : default;
                var @params = root.TryGetProperty("params", out var p) ? p : default;

                string resultJson;
                try
                {
                    resultJson = method switch
                    {
                        "TryInitialize" => ProxyState.Initialized
                            ? RequestHandler.FastInitResult(id)
                            : RequestHandler.HandleTryInitialize(invoker, id),
                        "Ok" => RequestHandler.HandleOk(invoker, @params, id),
                        "Call" => RequestHandler.HandleCall(invoker, @params, id),
                        "Ping" => """{"jsonrpc":"2.0","result":"pong","id":0}""",
                        "Shutdown" => null!, // handled below
                        _ => RequestHandler.ErrorResponse(-32601, $"Method not found: {method}", id),
                    };
                }
                catch (Exception ex)
                {
                    resultJson = RequestHandler.ErrorResponse(-32603, $"Internal error: {ex.GetType().Name}: {ex.Message}", id);
                }

                if (method == "Shutdown")
                {
                    await writer.WriteLineAsync(
                        """{"jsonrpc":"2.0","result":"shutting down","id":0}""");
                    // Signal the accept loop to stop accepting new connections.
                    // Other sessions will be terminated when the process exits.
                    shutdownCts.Cancel();
                    return;
                }

                await writer.WriteLineAsync(resultJson);
            }
            catch (JsonException ex)
            {
                await writer.WriteLineAsync(
                    RequestHandler.ErrorResponse(-32700, $"Parse error: {ex.Message}", default));
            }
        }
    }
    catch (OperationCanceledException)
    {
        // Shutdown requested — exit silently
    }
    catch (IOException)
    {
        // Client disconnected — clean up silently
    }
    finally
    {
        tracker.UnregisterSession(sessionId);
        pipe.Dispose();
    }
}
