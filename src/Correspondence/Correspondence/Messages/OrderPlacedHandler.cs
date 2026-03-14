using Marten;
using Messages.Contracts.Correspondence;
using Messages.Contracts.Orders;
using Wolverine;

namespace Correspondence.Messages;

/// <summary>
/// Handles OrderPlaced integration events to send order confirmation emails.
/// Choreography pattern: subscribes to OrderPlaced, creates Message aggregate, publishes CorrespondenceQueued.
/// </summary>
public sealed class OrderPlacedHandler
{
    public async Task<OutgoingMessages> Handle(
        OrderPlaced @event,
        IDocumentSession session,
        CancellationToken ct)
    {
        // TODO: Query Customer Identity BC for customer preferences
        // For now, assume customer has email notifications enabled
        var customerEmail = "customer@example.com"; // Will be populated from CustomerIdentity query

        // Template rendering will be enhanced in Phase 2
        var subject = $"Order Confirmation - Order #{@event.OrderId}";
        var body = $@"
            <html>
            <body>
                <h1>Thank you for your order!</h1>
                <p>Your order <strong>{@event.OrderId}</strong> has been placed successfully.</p>
                <p>Order Total: {@event.TotalAmount:C}</p>
                <p>We'll send you another email when your order ships.</p>
            </body>
            </html>
        ";

        // Create Message aggregate
        var (message, messageQueued) = MessageFactory.Create(
            customerId: @event.CustomerId,
            channel: "Email",
            templateId: "order-confirmation",
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

        // Trigger send command (will be executed by SendMessageHandler)
        outgoing.Add(new SendMessage(message.Id));

        return outgoing;
    }
}
