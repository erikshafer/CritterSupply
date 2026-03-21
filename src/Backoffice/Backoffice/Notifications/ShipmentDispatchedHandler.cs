using Marten;
using Messages.Contracts.Fulfillment;

namespace Backoffice.Notifications;

/// <summary>
/// Handler for ShipmentDispatched integration message from Fulfillment BC.
/// Appends message to Backoffice event store for FulfillmentPipelineView projection.
/// </summary>
public static class ShipmentDispatchedHandler
{
    public static void Handle(ShipmentDispatched message, IDocumentSession session)
    {
        session.Events.Append(Guid.NewGuid(), message);
    }
}
