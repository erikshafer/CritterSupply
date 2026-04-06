using Marten;

namespace Fulfillment.Shipments;

/// <summary>
/// Slice 25: Ghost shipment detection command.
/// Fired as a scheduled message 24 hours after ShipmentHandedToCarrier.
/// Checks if any carrier scan has arrived since handoff.
/// </summary>
public sealed record CheckForGhostShipment(
    Guid ShipmentId);

/// <summary>
/// Handler for detecting ghost shipments — no carrier scan 24h after handoff.
/// If ShipmentInTransit has been appended since handoff, the ghost check is a no-op.
/// Otherwise, appends GhostShipmentDetected.
/// </summary>
public static class CheckForGhostShipmentHandler
{
    public static async Task Handle(
        CheckForGhostShipment command,
        IDocumentSession session,
        CancellationToken ct)
    {
        var shipment = await session.LoadAsync<Shipment>(command.ShipmentId, ct);
        if (shipment is null) return;

        // Only fire if still in HandedToCarrier — if already InTransit or beyond, skip
        if (shipment.Status != ShipmentStatus.HandedToCarrier) return;

        // Idempotency: if already under investigation, skip
        if (shipment.Status == ShipmentStatus.GhostShipmentInvestigation) return;

        var timeSinceHandoff = DateTimeOffset.UtcNow - (shipment.HandedToCarrierAt ?? DateTimeOffset.UtcNow);

        session.Events.Append(command.ShipmentId,
            new GhostShipmentDetected(
                shipment.TrackingNumber ?? "unknown",
                timeSinceHandoff,
                DateTimeOffset.UtcNow));
    }
}
