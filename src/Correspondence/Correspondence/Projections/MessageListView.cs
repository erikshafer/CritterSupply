using Marten.Events.Aggregation;
using Correspondence.Messages;

namespace Correspondence.Projections;

/// <summary>
/// Inline projection for customer message history queries.
/// Supports GET /api/correspondence/messages/{customerId}
/// </summary>
public sealed record MessageListView
{
    public Guid Id { get; init; } // MessageId
    public Guid CustomerId { get; init; }
    public string Channel { get; init; } = string.Empty; // Email, SMS, Push
    public string Subject { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty; // Queued, Delivered, Failed, Skipped
    public DateTimeOffset QueuedAt { get; init; }
    public DateTimeOffset? DeliveredAt { get; init; }
    public int AttemptCount { get; init; }
    public string? FailureReason { get; init; }
}

public sealed class MessageListViewProjection : SingleStreamProjection<MessageListView, Guid>
{
    public MessageListView Create(MessageQueued @event)
    {
        return new MessageListView
        {
            Id = @event.MessageId,
            CustomerId = @event.CustomerId,
            Channel = @event.Channel,
            Subject = @event.Subject,
            Status = "Queued",
            QueuedAt = @event.QueuedAt,
            AttemptCount = 0
        };
    }

    public MessageListView Apply(MessageDelivered @event, MessageListView current)
    {
        return current with
        {
            Status = "Delivered",
            DeliveredAt = @event.DeliveredAt,
            AttemptCount = @event.AttemptNumber
        };
    }

    public MessageListView Apply(DeliveryFailed @event, MessageListView current)
    {
        return current with
        {
            Status = @event.AttemptNumber >= 3 ? "Failed" : "Queued",
            AttemptCount = @event.AttemptNumber,
            FailureReason = @event.ErrorMessage
        };
    }

    public MessageListView Apply(MessageSkipped @event, MessageListView current)
    {
        return current with
        {
            Status = "Skipped",
            FailureReason = @event.Reason
        };
    }
}
