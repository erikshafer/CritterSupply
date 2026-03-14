namespace Correspondence.Messages;

public sealed record DeliveryAttempt
{
    public int AttemptNumber { get; init; }
    public DateTimeOffset AttemptedAt { get; init; }
    public string ProviderResponse { get; init; } = string.Empty;
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
}
