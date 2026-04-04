using Messages.Contracts.Correspondence;
using Messages.Contracts.Returns;
using Wolverine;
using Wolverine.Marten;

namespace Correspondence.Messages;

/// <summary>
/// Handles ReturnDenied integration events to notify customers when their return request is rejected.
/// Choreography pattern: subscribes to ReturnDenied, creates Message aggregate, publishes CorrespondenceQueued.
/// </summary>
public static class ReturnDeniedHandler
{
    public static (IStartStream, OutgoingMessages) Handle(ReturnDenied @event)
    {
        // Template rendering will be enhanced with proper template system
        var subject = $"Return Request Update - Decision Required";
        var customerMessage = string.IsNullOrWhiteSpace(@event.Message)
            ? "We were unable to approve your return request at this time."
            : @event.Message;

        var body = $@"
            <html>
            <body>
                <h1>Return Request Status Update</h1>
                <p>We have reviewed your return request and need to provide you with an update.</p>
                <p><strong>Return ID:</strong> {@event.ReturnId}</p>
                <p><strong>Order ID:</strong> {@event.OrderId}</p>
                <p><strong>Decision Date:</strong> {@event.DeniedAt:f}</p>
                <h2>Reason:</h2>
                <p><strong>{@event.Reason}</strong></p>
                <p>{customerMessage}</p>
                <p>If you have questions about this decision, please contact our customer service team at support@crittersupply.com.</p>
                <p>Thank you for your understanding.</p>
            </body>
            </html>
        ";

        // Create Message aggregate
        var (message, messageQueued) = MessageFactory.Create(
            customerId: @event.CustomerId,
            channel: "Email",
            templateId: "return-denied",
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
