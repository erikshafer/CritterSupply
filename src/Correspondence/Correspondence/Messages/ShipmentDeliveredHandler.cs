using Messages.Contracts.Correspondence;
using Messages.Contracts.Fulfillment;
using Wolverine;
using Wolverine.Marten;

namespace Correspondence.Messages;

/// <summary>
/// Handles ShipmentDelivered integration events to send delivery confirmation emails.
/// Choreography pattern: subscribes to ShipmentDelivered, creates Message aggregate, publishes CorrespondenceQueued.
/// </summary>
public static class ShipmentDeliveredHandler
{
    public static (IStartStream, OutgoingMessages) Handle(ShipmentDelivered @event)
    {
        // TODO: Query Orders BC to get CustomerId for this OrderId
        // For Phase 2, we'll add cross-BC queries. Phase 1 uses placeholder.
        var customerId = Guid.Empty; // Placeholder - will be queried from Orders API

        // TODO: Query Customer Identity BC for customer preferences
        // For now, assume customer has email notifications enabled
        var customerEmail = "customer@example.com"; // Will be populated from CustomerIdentity query

        // Template rendering will be enhanced with proper template system
        var recipientDisplay = string.IsNullOrWhiteSpace(@event.RecipientName)
            ? "Customer"
            : @event.RecipientName;

        var subject = $"Your order has been delivered";
        var body = $@"
            <html>
            <body>
                <h1>Your order has arrived!</h1>
                <p>Hi {recipientDisplay},</p>
                <p>Great news! Your order has been successfully delivered.</p>
                <p><strong>Order ID:</strong> {@event.OrderId}</p>
                <p><strong>Delivered At:</strong> {@event.DeliveredAt:f}</p>
                <p>We hope you enjoy your purchase! If you have any issues, please contact customer service.</p>
                <p>Thank you for shopping with CritterSupply!</p>
            </body>
            </html>
        ";

        // Create Message aggregate
        var (message, messageQueued) = MessageFactory.Create(
            customerId: customerId,
            channel: "Email",
            templateId: "delivery-confirmation",
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
