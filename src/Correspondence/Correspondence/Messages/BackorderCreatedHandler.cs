using Messages.Contracts.Correspondence;
using Messages.Contracts.Fulfillment;
using Wolverine;
using Wolverine.Marten;

namespace Correspondence.Messages;

/// <summary>
/// Handles BackorderCreated integration events to send backorder notification emails.
/// Choreography pattern: subscribes to BackorderCreated, creates Message aggregate, publishes CorrespondenceQueued.
/// Added in M41.0 S5 — Correspondence enrichment for Fulfillment events.
/// </summary>
public static class BackorderCreatedHandler
{
    public static (IStartStream, OutgoingMessages) Handle(BackorderCreated @event)
    {
        // TODO: Query Customer Identity BC for customer preferences
        var customerId = Guid.Empty; // Placeholder - will be queried from Orders API

        var subject = "Update on your CritterSupply order — item backordered";

        var body = $@"
            <html>
            <body>
                <h1>Item Backordered</h1>
                <p>One or more items in your order <strong>{@event.OrderId}</strong> are currently out of stock at our fulfillment centers.</p>
                <p><strong>Reason:</strong> {@event.Reason}</p>
                <p>Your order is still active — we'll ship it as soon as stock is replenished.
                We'll send you a tracking number when it ships.</p>
                <p>If you'd prefer not to wait, you can cancel your order from your
                <a href=""#"">Order Status page</a>.</p>
            </body>
            </html>
        ";

        // Create Message aggregate
        var (message, messageQueued) = MessageFactory.Create(
            customerId: customerId,
            channel: "Email",
            templateId: "backorder-notification",
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
