using Marten;
using Messages.Contracts.Fulfillment;

namespace Backoffice.Notifications;

/// <summary>
/// Handler for ShipmentLostInTransit integration message from Fulfillment BC.
/// Appends message to Backoffice event store for AlertFeedView and FulfillmentPipelineView projections.
/// Added in M41.0 S5 for pipeline exception visibility.
/// </summary>
public static class ShipmentLostInTransitHandler
{
    public static void Handle(ShipmentLostInTransit message, IDocumentSession session)
    {
        session.Events.Append(Guid.NewGuid(), message);
    }
}
