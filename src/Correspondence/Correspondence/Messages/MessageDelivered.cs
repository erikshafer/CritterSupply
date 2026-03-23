namespace Correspondence.Messages;

/// <summary>
/// Domain Event: Message was successfully delivered via the provider.
/// Records the delivery timestamp, attempt number, and provider response.
/// </summary>
public sealed record MessageDelivered(
    Guid MessageId,
    DateTimeOffset DeliveredAt,
    int AttemptNumber,
    string ProviderResponse
);
