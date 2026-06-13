namespace aoc.Infrastructure;

public interface IAocInvoker
{
    // ── Sync methods (used by CLI path) ──
    bool TryInitialize(out string? diagnostic);
    object? Call(string method, params object?[] args);
    bool Ok(string method, params object?[] args);
    string? LastDiagnostic { get; }

    // ── Async methods (used by UI path, avoids Task.Run wrappers) ──
    Task<(bool Success, string? Diagnostic)> TryInitializeAsync();
    Task<object?> CallAsync(string method, params object?[] args);
    Task<bool> OkAsync(string method, params object?[] args);
}
