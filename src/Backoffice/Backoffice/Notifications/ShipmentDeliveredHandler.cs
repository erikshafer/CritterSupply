using Marten;
using Messages.Contracts.Fulfillment;

namespace Backoffice.Notifications;

/// <summary>
/// Handler for ShipmentDelivered integration message from Fulfillment BC.
/// Appends message to Backoffice event store for FulfillmentPipelineView projection.
/// </summary>
public static class ShipmentDeliveredHandler
{
    public static void Handle(ShipmentDelivered message, IDocumentSession session)
    {
        session.Events.Append(Guid.NewGuid(), message);
    }
}
