namespace Correspondence.Messages;

public sealed record Message
{
    public Guid Id { get; init; }
    public Guid CustomerId { get; init; }
    public string Channel { get; init; } = string.Empty; // Email, SMS, Push
    public string TemplateId { get; init; } = string.Empty;
    public string Subject { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public MessageStatus Status { get; init; }
    public int AttemptCount { get; init; }
    public DateTimeOffset QueuedAt { get; init; }
    public DateTimeOffset? DeliveredAt { get; init; }
    public IReadOnlyList<DeliveryAttempt> Attempts { get; init; } = [];

    // Apply methods (event sourcing pattern)
    public Message Apply(MessageQueued @event) => this with
    {
        Id = @event.MessageId,
        CustomerId = @event.CustomerId,
        Channel = @event.Channel,
        TemplateId = @event.TemplateId,
        Subject = @event.Subject,
        Body = @event.Body,
        Status = MessageStatus.Queued,
        QueuedAt = @event.QueuedAt
    };

    public Message Apply(MessageDelivered @event) => this with
    {
        Status = MessageStatus.Delivered,
        DeliveredAt = @event.DeliveredAt,
        Attempts = Attempts.Append(new DeliveryAttempt
        {
            AttemptNumber = @event.AttemptNumber,
            AttemptedAt = @event.DeliveredAt,
            Success = true,
            ProviderResponse = @event.ProviderResponse
        }).ToList()
    };

    public Message Apply(DeliveryFailed @event) => this with
    {
        Status = @event.AttemptNumber >= 3 ? MessageStatus.Failed : MessageStatus.Queued,
        AttemptCount = @event.AttemptNumber,
        Attempts = Attempts.Append(new DeliveryAttempt
        {
            AttemptNumber = @event.AttemptNumber,
            AttemptedAt = @event.FailedAt,
            Success = false,
            ErrorMessage = @event.ErrorMessage,
            ProviderResponse = @event.ProviderResponse
        }).ToList()
    };

    public Message Apply(MessageSkipped @event) => this with
    {
        Status = MessageStatus.Skipped
    };
}

public static class MessageFactory
{
    // Factory method: Create a new message
    public static (Message, MessageQueued) Create(
        Guid customerId,
        string channel,
        string templateId,
        string subject,
        string body)
    {
        var messageId = Guid.NewGuid();
        var @event = new MessageQueued(
            messageId,
            customerId,
            channel,
            templateId,
            subject,
            body,
            DateTimeOffset.UtcNow
        );

        var message = new Message
        {
            Id = messageId,
            CustomerId = customerId,
            Channel = channel,
            TemplateId = templateId,
            Subject = subject,
            Body = body,
            Status = MessageStatus.Queued,
            AttemptCount = 0,
            QueuedAt = @event.QueuedAt
        };

        return (message, @event);
    }

    // Factory method for skipped messages (customer opted out)
    public static (Message, MessageSkipped) Skip(
        Guid customerId,
        string reason)
    {
        var messageId = Guid.NewGuid();
        var @event = new MessageSkipped(messageId, reason);

        var message = new Message
        {
            Id = messageId,
            CustomerId = customerId,
            Status = MessageStatus.Skipped
        };

        return (message, @event);
    }
}
