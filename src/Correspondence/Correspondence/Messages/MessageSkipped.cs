namespace Correspondence.Messages;

/// <summary>
/// Domain Event: Message was skipped due to business rules.
/// Examples: Customer opted out, channel disabled, invalid destination.
/// </summary>
public sealed record MessageSkipped(
    Guid MessageId,
    string Reason // "Customer opted out of email" or "Channel disabled"
);
