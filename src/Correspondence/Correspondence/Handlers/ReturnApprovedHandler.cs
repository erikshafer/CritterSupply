using Correspondence.Messages;
using Marten;
using Messages.Contracts.Correspondence;
using Messages.Contracts.Returns;
using Wolverine;

namespace Correspondence.Handlers;

/// <summary>
/// Handles ReturnApproved integration events to send return label emails.
/// Choreography pattern: subscribes to ReturnApproved, creates Message aggregate, publishes CorrespondenceQueued.
/// </summary>
public sealed class ReturnApprovedHandler
{
    public async Task<OutgoingMessages> Handle(
        ReturnApproved @event,
        IDocumentSession session,
        CancellationToken ct)
    {
        // TODO: Query Customer Identity BC for customer preferences
        // For now, assume customer has email notifications enabled
        var customerEmail = "customer@example.com"; // Will be populated from CustomerIdentity query

        // Template rendering will be enhanced in Phase 2
        var subject = $"Return Approved - Return #{@event.ReturnId}";
        var body = $@"
            <html>
            <body>
                <h1>Your return has been approved</h1>
                <p>Your return request <strong>{@event.ReturnId}</strong> has been approved.</p>
                <p><strong>Estimated Refund:</strong> {@event.EstimatedRefundAmount:C}</p>
                <p><strong>Restocking Fee:</strong> {@event.RestockingFeeAmount:C}</p>
                <p><strong>Ship By:</strong> {@event.ShipByDeadline:MMMM dd, yyyy}</p>
                <p><strong>Instructions:</strong></p>
                <ol>
                    <li>Package your items securely</li>
                    <li>Print and attach the return label (link to be provided in Phase 2)</li>
                    <li>Drop off at any authorized shipping location</li>
                </ol>
                <p>We'll send you another email once we receive your return.</p>
            </body>
            </html>
        ";

        // Create Message aggregate
        var (message, messageQueued) = MessageFactory.Create(
            customerId: @event.CustomerId,
            channel: "Email",
            templateId: "return-label",
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
        outgoing.Add(new Commands.SendMessage(message.Id));

        return outgoing;
    }
}
