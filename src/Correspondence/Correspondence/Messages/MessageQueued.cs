namespace Correspondence.Messages;

/// <summary>
/// Domain Event: Message has been queued for delivery.
/// Marks the initial state when a message is created.
/// </summary>
public sealed record MessageQueued(
    Guid MessageId,
    Guid CustomerId,
    string Channel,
    string TemplateId,
    string Subject,
    string Body,
    DateTimeOffset QueuedAt
);
