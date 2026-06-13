namespace aoc.Domain;

public sealed record OperationResult(
    bool Success,
    ErrorKind ErrorKind,
    string UserMessage,
    string? Value = null,
    int? MaxValue = null,
    string? DiagnosticMessage = null)
{
    public static OperationResult Ok(string message, string? value = null, int? maxValue = null)
        => new(true, ErrorKind.None, message, value, maxValue);

    public static OperationResult Fail(ErrorKind kind, string message, string? diagnostic = null)
        => new(false, kind, message, null, null, diagnostic);
}
