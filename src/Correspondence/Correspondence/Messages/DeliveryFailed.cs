namespace Correspondence.Messages;

/// <summary>
/// Domain Event: Message delivery attempt failed.
/// Triggers exponential backoff retry logic (5min, 30min, 2hr).
/// </summary>
public sealed record DeliveryFailed(
    Guid MessageId,
    int AttemptNumber,
    DateTimeOffset FailedAt,
    string ErrorMessage,
    string ProviderResponse
);
