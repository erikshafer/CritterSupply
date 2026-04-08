using Messages.Contracts.Correspondence;
using Messages.Contracts.Fulfillment;
using Wolverine;
using Wolverine.Marten;

namespace Correspondence.Messages;

/// <summary>
/// Handles DeliveryAttemptFailed integration events to send delivery attempt notification emails.
/// Choreography pattern: subscribes to DeliveryAttemptFailed, creates Message aggregate, publishes CorrespondenceQueued.
/// Added in M41.0 S5 — Correspondence enrichment for Fulfillment events.
/// </summary>
public static class DeliveryAttemptFailedHandler
{
    public static (IStartStream, OutgoingMessages) Handle(DeliveryAttemptFailed @event)
    {
        // TODO: Query Customer Identity BC for customer preferences
        var customerId = Guid.Empty; // Placeholder - will be queried from Orders API

        var isFinalAttempt = @event.AttemptNumber >= 3;

        var subject = isFinalAttempt
            ? "Final delivery attempt for your CritterSupply order — action may be needed"
            : "Delivery attempt for your CritterSupply order";

        var body = isFinalAttempt
            ? $@"
                <html>
                <body>
                    <h1>Final Delivery Attempt</h1>
                    <p>This was the final delivery attempt for your order <strong>{@event.OrderId}</strong>.</p>
                    <p><strong>Carrier:</strong> {@event.Carrier}</p>
                    <p><strong>Reason:</strong> {@event.ExceptionDescription}</p>
                    <p>If no one can accept delivery, the package will be returned to us.
                    Please contact the carrier to arrange redelivery or pickup at a facility.</p>
                </body>
                </html>
            "
            : $@"
                <html>
                <body>
                    <h1>Delivery Attempt {@event.AttemptNumber}</h1>
                    <p>{@event.Carrier} attempted to deliver your package for order <strong>{@event.OrderId}</strong> today but was unable to.</p>
                    <p><strong>Reason:</strong> {@event.ExceptionDescription}</p>
                    <p>They will try again tomorrow. If you'd like to schedule a redelivery or arrange pickup, please contact the carrier directly.</p>
                </body>
                </html>
            ";

        // Create Message aggregate
        var (message, messageQueued) = MessageFactory.Create(
            customerId: customerId,
            channel: "Email",
            templateId: isFinalAttempt ? "delivery-final-attempt" : "delivery-attempt-failed",
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
