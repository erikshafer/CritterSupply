using Marten;
using Messages.Contracts.Fulfillment;

namespace Backoffice.Notifications;

/// <summary>
/// Integration message handler for ShipmentDeliveryFailed events from Fulfillment BC.
/// Appends message to Backoffice event store for AlertFeedView projection processing.
/// </summary>
public static class ShipmentDeliveryFailedHandler
{
    public static void Handle(ShipmentDeliveryFailed message, IDocumentSession session)
    {
        // Append integration message as event to Backoffice event store
        // AlertFeedView projection will process inline
        session.Events.Append(Guid.NewGuid(), message);
    }
}
