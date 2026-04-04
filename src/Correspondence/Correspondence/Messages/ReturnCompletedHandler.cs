using Messages.Contracts.Correspondence;
using Messages.Contracts.Returns;
using Wolverine;
using Wolverine.Marten;

namespace Correspondence.Messages;

/// <summary>
/// Handles ReturnCompleted integration events to send return received emails.
/// Choreography pattern: subscribes to ReturnCompleted, creates Message aggregate, publishes CorrespondenceQueued.
/// </summary>
public static class ReturnCompletedHandler
{
    public static (IStartStream, OutgoingMessages) Handle(ReturnCompleted @event)
    {
        // TODO: Query Customer Identity BC for customer preferences
        // For now, assume customer has email notifications enabled
        var customerEmail = "customer@example.com"; // Will be populated from CustomerIdentity query

        // Template rendering will be enhanced in Phase 2
        var subject = $"Return Received - Return #{@event.ReturnId}";
        var body = $@"
            <html>
            <body>
                <h1>We've received your return</h1>
                <p>Your return <strong>{@event.ReturnId}</strong> has been received and processed.</p>
                <p><strong>Refund Amount:</strong> {@event.FinalRefundAmount:C}</p>
                <p>Your refund will be processed within 3-5 business days and will appear in your original payment method.</p>
                <p>Thank you for shopping with CritterSupply!</p>
            </body>
            </html>
        ";

        // Create Message aggregate
        var (message, messageQueued) = MessageFactory.Create(
            customerId: @event.CustomerId,
            channel: "Email",
            templateId: "return-completed",
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
            @event.CustomerId,
            "Email",
            messageQueued.QueuedAt
        ));

        // Trigger send command
        outgoing.Add(new SendMessage(message.Id));

        return (stream, outgoing);
    }
}
