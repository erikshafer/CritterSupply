using Marten;
using Wolverine;
using IntegrationMessages = Messages.Contracts.Fulfillment;

namespace Fulfillment.Shipments;

/// <summary>
/// Slice 26: Shipment lost in transit detection command.
/// Fired as a scheduled message 5 business days after ShipmentHandedToCarrier.
/// </summary>
public sealed record CheckForLostShipment(
    Guid ShipmentId);

/// <summary>
/// Handler for detecting shipments lost in transit.
/// If no carrier scan has arrived within the threshold, appends ShipmentLostInTransit
/// and CarrierTraceOpened events. Publishes ShipmentLostInTransit integration event.
/// </summary>
public static class CheckForLostShipmentHandler
{
    // Stub: 5 business days ≈ 7 calendar days / 1.4
    private static readonly TimeSpan LostThreshold = TimeSpan.FromDays(7);

    public static async Task Handle(
        CheckForLostShipment command,
        IDocumentSession session,
        IMessageBus bus,
        CancellationToken ct)
    {
        var shipment = await session.LoadAsync<Shipment>(command.ShipmentId, ct);
        if (shipment is null) return;

        // Only fire for shipments still in HandedToCarrier or GhostShipmentInvestigation
        if (shipment.Status is not (ShipmentStatus.HandedToCarrier or ShipmentStatus.GhostShipmentInvestigation))
            return;

        var timeSinceHandoff = DateTimeOffset.UtcNow - (shipment.HandedToCarrierAt ?? DateTimeOffset.UtcNow);

        // Idempotency: if already lost, skip
        if (shipment.Status == ShipmentStatus.LostInTransit) return;

        if (timeSinceHandoff >= LostThreshold)
        {
            var now = DateTimeOffset.UtcNow;
            var carrier = shipment.Carrier ?? "Unknown";
            var traceRef = $"TRACE-{shipment.TrackingNumber ?? "UNK"}-{Guid.NewGuid():N}"[..30];

            session.Events.Append(command.ShipmentId,
                new ShipmentLostInTransit(carrier, timeSinceHandoff, now),
                new CarrierTraceOpened(carrier, 15, traceRef, now));

            // Publish integration event to Orders saga
            await bus.PublishAsync(new IntegrationMessages.ShipmentLostInTransit(
                shipment.OrderId,
                command.ShipmentId,
                carrier,
                timeSinceHandoff,
                now));
        }
    }
}
