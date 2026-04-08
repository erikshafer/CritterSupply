using Marten;
using Messages.Contracts.Fulfillment;

namespace Backoffice.Notifications;

/// <summary>
/// Handler for ShipmentHandedToCarrier integration message from Fulfillment BC.
/// Appends message to Backoffice event store for FulfillmentPipelineView projection.
/// Replaces ShipmentDispatchedHandler (retired in M41.0 S5).
/// </summary>
public static class ShipmentHandedToCarrierHandler
{
    public static void Handle(ShipmentHandedToCarrier message, IDocumentSession session)
    {
        session.Events.Append(Guid.NewGuid(), message);
    }
}
