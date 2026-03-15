using Marten;
using Messages.Contracts.Correspondence;
using Messages.Contracts.Returns;
using Wolverine;

namespace Correspondence.Messages;

/// <summary>
/// Handles ReturnExpired integration events to notify customers when their return window has closed.
/// Choreography pattern: subscribes to ReturnExpired, creates Message aggregate, publishes CorrespondenceQueued.
/// </summary>
public sealed class ReturnExpiredHandler
{
    public async Task<OutgoingMessages> Handle(
        ReturnExpired @event,
        IDocumentSession session,
        CancellationToken ct)
    {
        // Template rendering will be enhanced with proper template system
        var subject = $"Return Window Expired - No Action Taken";
        var body = $@"
            <html>
            <body>
                <h1>Return Window Expired</h1>
                <p>This is a notification that your return request has expired.</p>
                <p><strong>Return ID:</strong> {@event.ReturnId}</p>
                <p><strong>Order ID:</strong> {@event.OrderId}</p>
                <p><strong>Expired At:</strong> {@event.ExpiredAt:f}</p>
                <p>We approved your return request, but did not receive the returned items within the allowed timeframe.</p>
                <p>As a result, your return request has been closed and no refund will be issued.</p>
                <p>If you have any questions or believe this was in error, please contact customer service at support@crittersupply.com.</p>
                <p>We value your business and hope to serve you again soon.</p>
            </body>
            </html>
        ";

        // Create Message aggregate
        var (message, messageQueued) = MessageFactory.Create(
            customerId: @event.CustomerId,
            channel: "Email",
            templateId: "return-expired",
            subject: subject,
            body: body
        );

        // Persist event stream
        session.Events.StartStream<Message>(message.Id, messageQueued);

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

        return outgoing;
    }
}
