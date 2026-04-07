using Messages.Contracts.Correspondence;
using Messages.Contracts.Fulfillment;
using Wolverine;
using Wolverine.Marten;

namespace Correspondence.Messages;

/// <summary>
/// Handles ReturnToSenderInitiated integration events from Fulfillment BC.
/// Sends customer notification: "Your package is being returned — we'll be in touch."
/// Choreography pattern: subscribes to ReturnToSenderInitiated, creates Message aggregate, publishes CorrespondenceQueued.
/// Replaces ShipmentDeliveryFailedHandler (retired in M41.0 S4).
/// </summary>
public static class ReturnToSenderInitiatedHandler
{
    public static (IStartStream, OutgoingMessages) Handle(ReturnToSenderInitiated @event)
    {
        // TODO: Query Orders BC to get CustomerId for this OrderId
        // For Phase 2, we'll add cross-BC queries. Phase 1 uses placeholder.
        var customerId = Guid.Empty; // Placeholder - will be queried from Orders API

        // Template rendering will be enhanced with proper template system
        var subject = "Your package is being returned to us";
        var body = $@"
            <html>
            <body>
                <h1>Delivery Update</h1>
                <p>After {@event.TotalAttempts} delivery attempt(s), the carrier is returning your package to us.</p>
                <p><strong>Order ID:</strong> {@event.OrderId}</p>
                <p><strong>Carrier:</strong> {@event.Carrier}</p>
                <p>We'll contact you shortly about reshipping or issuing a refund.</p>
                <p>If you have any questions, please reach out to us at support@crittersupply.com.</p>
            </body>
            </html>
        ";

        // Create Message aggregate
        var (message, messageQueued) = MessageFactory.Create(
            customerId: customerId,
            channel: "Email",
            templateId: "return-to-sender",
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
