using Messages.Contracts.Correspondence;
using Messages.Contracts.Payments;
using Wolverine;
using Wolverine.Marten;

namespace Correspondence.Messages;

/// <summary>
/// Handles RefundCompleted integration events to notify customers when their refund has been processed.
/// Choreography pattern: subscribes to RefundCompleted, creates Message aggregate, publishes CorrespondenceQueued.
/// </summary>
public static class RefundCompletedHandler
{
    public static (IStartStream, OutgoingMessages) Handle(RefundCompleted @event)
    {
        // TODO: Query Orders BC to get CustomerId for this OrderId
        // For Phase 2, we'll add cross-BC queries. Phase 1 uses placeholder.
        var customerId = Guid.Empty; // Placeholder - will be queried from Orders API

        // TODO: Query Customer Identity BC for customer preferences
        // For now, assume customer has email notifications enabled
        var customerEmail = "customer@example.com"; // Will be populated from CustomerIdentity query

        // Template rendering will be enhanced with proper template system
        var subject = $"Refund Processed - ${@event.Amount:F2}";
        var body = $@"
            <html>
            <body>
                <h1>Your Refund Has Been Processed</h1>
                <p>Good news! Your refund has been successfully processed.</p>
                <p><strong>Order ID:</strong> {@event.OrderId}</p>
                <p><strong>Refund Amount:</strong> ${@event.Amount:F2}</p>
                <p><strong>Transaction ID:</strong> {@event.TransactionId}</p>
                <p><strong>Processed At:</strong> {@event.RefundedAt:f}</p>
                <p>The refund will appear in your original payment method within 5-10 business days, depending on your financial institution.</p>
                <p>If you have any questions about this refund, please contact customer service at support@crittersupply.com.</p>
                <p>Thank you for your patience!</p>
            </body>
            </html>
        ";

        // Create Message aggregate
        var (message, messageQueued) = MessageFactory.Create(
            customerId: customerId,
            channel: "Email",
            templateId: "refund-completed",
            subject: subject,
            body: body
        );

        // Wolverine handles stream creation transactionally via IStartStream return
        var stream = MartenOps.StartStream<Message>(message.Id, messageQueued);

        // Build outgoing messages
        var outgoing = new OutgoingMessages();

        // Publish integration event for monitoring
        outgoing.Add(new CorrespondenceQueued(
            message.Id,
            customerId,
            "Email",
            messageQueued.QueuedAt
        ));

        // Trigger send command
        outgoing.Add(new SendMessage(message.Id));

        return (stream, outgoing);
    }
}
