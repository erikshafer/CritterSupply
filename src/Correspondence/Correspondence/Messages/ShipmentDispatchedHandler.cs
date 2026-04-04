using Messages.Contracts.Correspondence;
using Messages.Contracts.Fulfillment;
using Wolverine;
using Wolverine.Marten;

namespace Correspondence.Messages;

/// <summary>
/// Handles ShipmentDispatched integration events to send tracking emails.
/// Choreography pattern: subscribes to ShipmentDispatched, creates Message aggregate, publishes CorrespondenceQueued.
/// </summary>
public static class ShipmentDispatchedHandler
{
    public static (IStartStream, OutgoingMessages) Handle(ShipmentDispatched @event)
    {
        // TODO: Query Orders BC to get CustomerId for this OrderId
        // For Phase 1, we'll use a placeholder. Phase 2 will add cross-BC queries.
        var customerId = Guid.Empty; // Placeholder - will be queried from Orders API

        // TODO: Query Customer Identity BC for customer preferences
        // For now, assume customer has email notifications enabled
        var customerEmail = "customer@example.com"; // Will be populated from CustomerIdentity query

        // Template rendering will be enhanced in Phase 2
        var subject = $"Your order has shipped - Tracking #{@event.TrackingNumber}";
        var body = $@"
            <html>
            <body>
                <h1>Your order is on the way!</h1>
                <p>Your order <strong>{@event.OrderId}</strong> has been shipped.</p>
                <p><strong>Tracking Number:</strong> {@event.TrackingNumber}</p>
                <p><strong>Carrier:</strong> {@event.Carrier}</p>
                <p>You can track your shipment using the tracking number above.</p>
            </body>
            </html>
        ";

        // Create Message aggregate
        var (message, messageQueued) = MessageFactory.Create(
            customerId: customerId,
            channel: "Email",
            templateId: "shipment-tracking",
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
