using System.Collections.Concurrent;

namespace aoc.SdkProxy;

/// <summary>
/// Tracks active pipe sessions and coordinates idle timeout for the multi-client proxy.
///
/// Thread safety:
/// - Session registration/unregistration uses ConcurrentDictionary internally.
/// - The idle CTS and active-wait-CTS fields are guarded by _lock to avoid torn reads
///   when the accept-loop thread and session-handler tasks access them concurrently.
///
/// Lifecycle:
/// When the last session disconnects, SessionTracker creates an idle-timeout CTS and
/// cancels the accept loop's current WaitForConnectionAsync. This forces the accept
/// loop to restart its wait with the idle timeout token. If a new client connects
/// before the idle timeout fires, RegisterSession cancels the idle CTS and the proxy
/// continues normally. If the idle timeout fires, the accept loop exits the proxy.
/// </summary>
public sealed class SessionTracker : IDisposable
{
    private readonly ConcurrentDictionary<Guid, bool> _sessions = new();
    private readonly object _lock = new();
    private readonly int _idleTimeoutSec;

    // ── Idle timeout CTS — created when count reaches 0, cancelled on new session ─
    private CancellationTokenSource? _idleCts;

    // ── Active wait CTS — set by the accept loop each iteration ──────────────────
    private CancellationTokenSource? _activeWaitCts;

    public bool IsIdle => _sessions.IsEmpty;

    public int ActiveSessionCount => _sessions.Count;

    public SessionTracker(int idleTimeoutSec = 30)
    {
        _idleTimeoutSec = idleTimeoutSec;
    }

    /// <summary>
    /// Register a new session. Returns the session GUID.
    /// If an idle timeout was pending, cancels it.
    /// </summary>
    public Guid RegisterSession()
    {
        var id = Guid.NewGuid();
        _sessions.TryAdd(id, true);

        // Cancel any pending idle timeout — a new session connected
        lock (_lock)
        {
            _idleCts?.Cancel();
            _idleCts?.Dispose();
            _idleCts = null;
        }

        return id;
    }

    /// <summary>
    /// Unregister a session. If this was the last session, creates an idle timeout
    /// CTS and cancels the current WaitForConnectionAsync so the accept loop
    /// restarts with idle timeout protection.
    /// </summary>
    public void UnregisterSession(Guid id)
    {
        _sessions.TryRemove(id, out _);

        if (!_sessions.IsEmpty)
            return;

        // Became idle — create idle CTS and cancel the current accept wait
        CancellationTokenSource? waitToCancel;
        lock (_lock)
        {
            _idleCts = new CancellationTokenSource(TimeSpan.FromSeconds(_idleTimeoutSec));
            waitToCancel = _activeWaitCts;
        }

        // Cancel the accept loop's current wait so it restarts with idle timeout
        waitToCancel?.Cancel();
    }

    /// <summary>
    /// Creates a CancellationTokenSource for the accept loop's WaitForConnectionAsync.
    /// When sessions are active, the CTS is linked only to shutdownCt.
    /// When idle, the CTS includes the idle timeout so the wait auto-expires.
    ///
    /// The caller MUST dispose the returned CTS after the wait completes (success or OCE).
    /// MUST call <see cref="ClearWaitCts"/> after dispose to clear the internal reference.
    /// </summary>
    public CancellationTokenSource CreateWaitCts(CancellationToken shutdownCt)
    {
        lock (_lock)
        {
            var hasIdle = _sessions.IsEmpty && _idleCts is not null;
            _activeWaitCts = hasIdle
                ? CancellationTokenSource.CreateLinkedTokenSource(shutdownCt, _idleCts!.Token)
                : CancellationTokenSource.CreateLinkedTokenSource(shutdownCt);
            return _activeWaitCts;
        }
    }

    /// <summary>Clear the active wait CTS reference (called after WaitForConnectionAsync completes).</summary>
    public void ClearWaitCts()
    {
        lock (_lock)
        {
            _activeWaitCts = null;
        }
    }

    /// <summary>Cancel any pending idle timeout.</summary>
    public void CancelIdle()
    {
        lock (_lock)
        {
            _idleCts?.Cancel();
            _idleCts?.Dispose();
            _idleCts = null;
        }
    }

    public void Dispose()
    {
        CancelIdle();
        _activeWaitCts = null;
        _sessions.Clear();
    }
}
