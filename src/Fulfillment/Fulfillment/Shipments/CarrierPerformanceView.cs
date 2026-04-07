using Marten.Events.Projections;

namespace Fulfillment.Shipments;

/// <summary>
/// Read model tracking carrier performance metrics.
/// Keyed by carrier name. Populated from carrier lifecycle events.
/// </summary>
public sealed class CarrierPerformanceView
{
    public string Id { get; set; } = "";
    public int TotalShipments { get; set; }
    public int GhostShipments { get; set; }
    public int LostShipments { get; set; }
    public int OpenClaims { get; set; }
    public int ResolvedClaims { get; set; }
    public int RateDisputes { get; set; }
    public int MissedPickups { get; set; }
}

/// <summary>
/// Multi-stream projection that builds CarrierPerformanceView from carrier lifecycle events.
/// Keyed by carrier name.
/// </summary>
public sealed class CarrierPerformanceViewProjection : MultiStreamProjection<CarrierPerformanceView, string>
{
    public CarrierPerformanceViewProjection()
    {
        Identity<ShipmentHandedToCarrier>(e => e.Carrier);
        Identity<GhostShipmentDetected>(e =>
        {
            // GhostShipmentDetected doesn't carry Carrier directly — we derive from tracking number prefix
            // Stub: use "Unknown" as carrier key
            return "Unknown";
        });
        Identity<ShipmentLostInTransit>(e => e.Carrier);
        Identity<CarrierClaimFiled>(e => e.Carrier);
        Identity<CarrierClaimResolved>(_ => "Unknown"); // Will be resolved from the stream
        Identity<RateDisputeRaised>(e => e.Carrier);
        Identity<CarrierPickupMissed>(e => e.Carrier);
    }

    public CarrierPerformanceView Create(ShipmentHandedToCarrier @event) =>
        new() { Id = @event.Carrier, TotalShipments = 1 };

    public void Apply(ShipmentHandedToCarrier @event, CarrierPerformanceView view)
    {
        view.TotalShipments++;
    }

    public void Apply(GhostShipmentDetected _, CarrierPerformanceView view)
    {
        view.GhostShipments++;
    }

    public void Apply(ShipmentLostInTransit _, CarrierPerformanceView view)
    {
        view.LostShipments++;
    }

    public void Apply(CarrierClaimFiled _, CarrierPerformanceView view)
    {
        view.OpenClaims++;
    }

    public void Apply(CarrierClaimResolved _, CarrierPerformanceView view)
    {
        view.OpenClaims = Math.Max(0, view.OpenClaims - 1);
        view.ResolvedClaims++;
    }

    public void Apply(RateDisputeRaised _, CarrierPerformanceView view)
    {
        view.RateDisputes++;
    }

    public void Apply(CarrierPickupMissed _, CarrierPerformanceView view)
    {
        view.MissedPickups++;
    }
}
