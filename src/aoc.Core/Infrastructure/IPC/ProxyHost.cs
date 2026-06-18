namespace aoc.Infrastructure.IPC;

/// <summary>
/// Manages the lifecycle of the 32-bit ZeasnProxy child process.
/// 
/// On startup, tries to reuse an already-running ZeasnProxy instance
/// (identified via a named EventWaitHandle). Only starts a new process
/// when no existing proxy is found. This avoids paying the SDK DLL loading
/// and AOCOper initialization cost on every CLI invocation.
/// 
/// The proxy process manages its own lifetime via a 30-second idle timeout,
/// so DisposeAsync no longer kills it — the proxy stays alive to serve
/// subsequent CLI invocations and auto-exits after inactivity.
/// </summary>
public sealed class ProxyHost : IAsyncDisposable
{
    private Process? _process;
    private bool _disposed;
    /// <summary>Whether we spawned the process (vs. reusing an existing one).</summary>
    private bool _ownsProcess;

    /// <summary>The named pipe name the proxy is listening on.</summary>
    public string PipeName { get; } = "ZeasnProxy";

    /// <summary>
    /// Ensures a ZeasnProxy instance is running. If an existing instance is detected
    /// via the "ZeasnProxy_Running" EventWaitHandle, no new process is started.
    /// Otherwise, starts ZeasnProxy.exe and waits for its readiness signal.
    /// </summary>
    /// <param name="timeout">Maximum time to wait for proxy startup when a new process is created.</param>
    /// <exception cref="TimeoutException">Thrown if proxy does not start within timeout.</exception>
    /// <exception cref="InvalidOperationException">Thrown if proxy exits or fails to initialize.</exception>
    public async Task StartAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        // Fast path: an existing proxy instance is already running.
        if (IsProxyRunning())
        {
            _ownsProcess = false;
            return;
        }

        var proxyPath = ResolveProxyPath();
        if (!File.Exists(proxyPath))
            throw new InvalidOperationException($"ZeasnProxy.exe not found at: {proxyPath}");

        _process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = proxyPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
            },
            EnableRaisingEvents = true,
        };

        _process.Start();
        _ownsProcess = true;

        // Read ready signal from stderr ("Proxy started, pipe=..., pid=N")
        var readyLine = await _process.StandardError.ReadLineAsync(ct)
            .AsTask().WaitAsync(timeout, ct);

        if (readyLine is null || !readyLine.StartsWith("Proxy started"))
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                    await _process.WaitForExitAsync();
                }
            }
            catch { /* best-effort cleanup */ }

            var exitCode = _process.HasExited ? _process.ExitCode : -1;
            throw new InvalidOperationException(
                $"Proxy failed to start. Exit code: {exitCode}. Output: {readyLine ?? "(none)"}");
        }
    }

    /// <summary>
    /// Detects whether a ZeasnProxy instance is already listening, using a
    /// named EventWaitHandle that the proxy creates on startup.
    /// </summary>
    private static bool IsProxyRunning()
    {
        if (!OperatingSystem.IsWindows())
            return false;

        try
        {
            if (EventWaitHandle.TryOpenExisting("ZeasnProxy_Running", out var handle))
            {
                handle.Dispose();
                return true;
            }
        }
        catch
        {
            // Ignore errors — treat as "not running" and fall through to start a new proxy.
        }
        return false;
    }

    /// <summary>
    /// Resolves the path to ZeasnProxy.exe.
    /// First checks the host app directory (for simple colocated deployment).
    /// Falls back to the x86\ subfolder where the build target
    /// _CopyZeasnProxyOutput in AOC.UI.csproj places the self-contained x86
    /// runtime files, avoiding native DLL name conflicts with the x64 host.
    /// </summary>
    private static string ResolveProxyPath()
    {
        // 1. Check next to the host app (packaged deployment or manual copy).
        var basePath = Path.Combine(AppContext.BaseDirectory, "ZeasnProxy.exe");
        if (File.Exists(basePath))
            return basePath;

        // 2. Fallback: x86\ subfolder (self-contained runtime, no architecture conflict).
        var subPath = Path.Combine(AppContext.BaseDirectory, "x86", "ZeasnProxy.exe");
        if (File.Exists(subPath))
            return subPath;

        // 3. Return basePath and let the caller handle the error.
        return basePath;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        // If we own the proxy process, detach instead of killing it.
        // The proxy has a built-in idle timeout (30 s) and will exit on its own.
        // This allows a subsequent CLI invocation to reuse the SDK initialization.
        if (_process is not null && _ownsProcess)
        {
            // Detach — the child process continues independently.
            try { _process.Close(); } catch { /* best-effort */ }
        }

        _process?.Dispose();
        _process = null;
    }
}
