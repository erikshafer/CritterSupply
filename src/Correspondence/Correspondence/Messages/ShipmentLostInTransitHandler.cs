using Messages.Contracts.Correspondence;
using Messages.Contracts.Fulfillment;
using Wolverine;
using Wolverine.Marten;

namespace Correspondence.Messages;

/// <summary>
/// Handles ShipmentLostInTransit integration events to send lost shipment notification emails.
/// Choreography pattern: subscribes to ShipmentLostInTransit, creates Message aggregate, publishes CorrespondenceQueued.
/// Added in M41.0 S5 — Correspondence enrichment for Fulfillment events.
///
/// CritterSupply policy: reship immediately and don't wait for carrier trace to resolve.
/// The notification sets customer expectations about the replacement shipment.
/// </summary>
public static class ShipmentLostInTransitHandler
{
    public static (IStartStream, OutgoingMessages) Handle(ShipmentLostInTransit @event)
    {
        // TODO: Query Customer Identity BC for customer preferences
        var customerId = Guid.Empty; // Placeholder - will be queried from Orders API

        var subject = "We're looking into your shipment — a replacement is on its way";

        var body = $@"
            <html>
            <body>
                <h1>Shipment Update</h1>
                <p>We've been unable to track your shipment for order <strong>{@event.OrderId}</strong> for several business days
                (carrier: {@event.Carrier}).</p>
                <p>We're working with the carrier to locate your package, and we've already sent you a
                replacement at no charge — you'll receive a new tracking number shortly.</p>
                <p>If your original package does arrive, you're welcome to keep it.</p>
            </body>
            </html>
        ";

        // Create Message aggregate
        var (message, messageQueued) = MessageFactory.Create(
            customerId: customerId,
            channel: "Email",
            templateId: "shipment-lost-in-transit",
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
