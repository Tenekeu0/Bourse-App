namespace BourseApplication;

public sealed class OrderValidationResult
{
    public bool IsValid { get; init; }
    public string Reason { get; init; } = string.Empty;

    public static OrderValidationResult Success() => new() { IsValid = true };

    public static OrderValidationResult Failure(string reason) => new()
    {
        IsValid = false,
        Reason = reason
    };
}
