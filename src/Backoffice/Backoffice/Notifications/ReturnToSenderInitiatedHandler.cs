using Marten;
using Messages.Contracts.Fulfillment;

namespace Backoffice.Notifications;

/// <summary>
/// Handler for ReturnToSenderInitiated integration message from Fulfillment BC.
/// Appends message to Backoffice event store for AlertFeedView and FulfillmentPipelineView projections.
/// Replaces ShipmentDeliveryFailedHandler (retired in M41.0 S5).
/// </summary>
public static class ReturnToSenderInitiatedHandler
{
    public static void Handle(ReturnToSenderInitiated message, IDocumentSession session)
    {
        session.Events.Append(Guid.NewGuid(), message);
    }
}
