using Messages.Contracts.Correspondence;
using Messages.Contracts.Fulfillment;
using Wolverine;
using Wolverine.Marten;

namespace Correspondence.Messages;

/// <summary>
/// Handles ShipmentDeliveryFailed integration events to alert customers about delivery issues.
/// Choreography pattern: subscribes to ShipmentDeliveryFailed, creates Message aggregate, publishes CorrespondenceQueued.
/// </summary>
public static class ShipmentDeliveryFailedHandler
{
    public static (IStartStream, OutgoingMessages) Handle(ShipmentDeliveryFailed @event)
    {
        // TODO: Query Orders BC to get CustomerId for this OrderId
        // For Phase 2, we'll add cross-BC queries. Phase 1 uses placeholder.
        var customerId = Guid.Empty; // Placeholder - will be queried from Orders API

        // TODO: Query Customer Identity BC for customer preferences
        // For now, assume customer has email notifications enabled
        var customerEmail = "customer@example.com"; // Will be populated from CustomerIdentity query

        // Template rendering will be enhanced with proper template system
        var subject = $"Action required: Delivery issue with your order";
        var body = $@"
            <html>
            <body>
                <h1>Delivery Issue Alert</h1>
                <p>We're sorry, but there was an issue delivering your order.</p>
                <p><strong>Order ID:</strong> {@event.OrderId}</p>
                <p><strong>Issue:</strong> {@event.Reason}</p>
                <p><strong>Failed At:</strong> {@event.FailedAt:f}</p>
                <p>Our customer service team will contact you shortly to resolve this issue.</p>
                <p>In the meantime, if you have any questions, please reach out to us at support@crittersupply.com.</p>
            </body>
            </html>
        ";

        // Create Message aggregate
        var (message, messageQueued) = MessageFactory.Create(
            customerId: customerId,
            channel: "Email",
            templateId: "delivery-failed",
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
