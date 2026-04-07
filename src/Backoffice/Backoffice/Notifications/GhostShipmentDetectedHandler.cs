using Marten;
using Messages.Contracts.Fulfillment;

namespace Backoffice.Notifications;

/// <summary>
/// Handler for GhostShipmentDetected integration message from Fulfillment BC.
/// Appends message to Backoffice event store for AlertFeedView projection.
/// Added in M41.0 S5 — ghost shipments require immediate ops attention.
/// </summary>
public static class GhostShipmentDetectedHandler
{
    public static void Handle(GhostShipmentDetected message, IDocumentSession session)
    {
        session.Events.Append(Guid.NewGuid(), message);
    }
}
