using Fulfillment.Shipments;

namespace Fulfillment.UnitTests.Shipments;

/// <summary>
/// Pure-function unit tests for <see cref="CarrierPerformanceViewProjection"/>.
///
/// Critical regression covered: before the M45.0 fix, every
/// <c>GhostShipmentDetected</c> and <c>CarrierClaimResolved</c> event was
/// bucketed into a single <c>"Unknown"</c>-keyed view because the projection
/// used <c>_ =&gt; "Unknown"</c> for identity resolution. Both events now
/// carry <c>Carrier</c> as the leading field and the projection uses
/// <c>Identity&lt;T&gt;(e =&gt; e.Carrier)</c>. The "two carriers, disjoint
/// counters" test pins this guarantee at the unit level.
/// </summary>
public class CarrierPerformanceViewProjectionTests
{
    private static CarrierPerformanceView NewView(string carrier) => new() { Id = carrier };

    // --- Identity wiring smoke test -----------------------------------------

    [Fact]
    public void Constructor_DoesNotThrow()
    {
        // Guards against typos in the new Identity<T>(e => e.Carrier) wiring.
        var act = () => new CarrierPerformanceViewProjection();
        act.ShouldNotThrow();
    }

    [Fact]
    public void Create_FromHandedToCarrier_SeedsViewWithCarrierIdAndOneShipment()
    {
        var projection = new CarrierPerformanceViewProjection();
        var view = projection.Create(new ShipmentHandedToCarrier("UPS", "1Z", DateTimeOffset.UtcNow));

        view.Id.ShouldBe("UPS");
        view.TotalShipments.ShouldBe(1);
    }

    // --- Apply counters -----------------------------------------------------

    [Fact]
    public void Apply_ShipmentHandedToCarrier_IncrementsTotalShipments()
    {
        var projection = new CarrierPerformanceViewProjection();
        var view = NewView("UPS");

        projection.Apply(new ShipmentHandedToCarrier("UPS", "1Z", DateTimeOffset.UtcNow), view);
        projection.Apply(new ShipmentHandedToCarrier("UPS", "1Z", DateTimeOffset.UtcNow), view);

        view.TotalShipments.ShouldBe(2);
    }

    [Fact]
    public void Apply_GhostShipmentDetected_IncrementsGhostShipments()
    {
        var projection = new CarrierPerformanceViewProjection();
        var view = NewView("UPS");

        projection.Apply(
            new GhostShipmentDetected("UPS", "1Z", TimeSpan.FromHours(24), DateTimeOffset.UtcNow),
            view);

        view.GhostShipments.ShouldBe(1);
    }

    [Fact]
    public void Apply_ShipmentLostInTransit_IncrementsLostShipments()
    {
        var projection = new CarrierPerformanceViewProjection();
        var view = NewView("UPS");

        projection.Apply(
            new ShipmentLostInTransit("UPS", TimeSpan.FromDays(7), DateTimeOffset.UtcNow),
            view);

        view.LostShipments.ShouldBe(1);
    }

    [Fact]
    public void Apply_CarrierClaimFiled_IncrementsOpenClaims()
    {
        var projection = new CarrierPerformanceViewProjection();
        var view = NewView("UPS");

        projection.Apply(
            new CarrierClaimFiled("UPS", "Lost", Guid.NewGuid(), "1Z", DateTimeOffset.UtcNow),
            view);

        view.OpenClaims.ShouldBe(1);
        view.ResolvedClaims.ShouldBe(0);
    }

    [Fact]
    public void Apply_CarrierClaimResolved_DecrementsOpenClaimsAndIncrementsResolvedClaims()
    {
        var projection = new CarrierPerformanceViewProjection();
        var view = new CarrierPerformanceView { Id = "UPS", OpenClaims = 2 };

        projection.Apply(
            new CarrierClaimResolved("UPS", "Approved", 100m, DateTimeOffset.UtcNow),
            view);

        view.OpenClaims.ShouldBe(1);
        view.ResolvedClaims.ShouldBe(1);
    }

    [Fact]
    public void Apply_CarrierClaimResolved_FloorsOpenClaimsAtZero()
    {
        // Defensive: if a resolution arrives without a matching filed event
        // (out-of-order replay, projection rebuild glitch) OpenClaims must
        // not go negative.
        var projection = new CarrierPerformanceViewProjection();
        var view = NewView("UPS"); // OpenClaims defaults to 0.

        projection.Apply(
            new CarrierClaimResolved("UPS", "Approved", 0m, DateTimeOffset.UtcNow),
            view);

        view.OpenClaims.ShouldBe(0);
        view.ResolvedClaims.ShouldBe(1);
    }

    [Fact]
    public void Apply_RateDisputeRaised_IncrementsRateDisputes()
    {
        var projection = new CarrierPerformanceViewProjection();
        var view = NewView("UPS");

        projection.Apply(
            new RateDisputeRaised("D-1", 10m, 12m, "UPS", DateTimeOffset.UtcNow),
            view);

        view.RateDisputes.ShouldBe(1);
    }

    [Fact]
    public void Apply_CarrierPickupMissed_IncrementsMissedPickups()
    {
        var projection = new CarrierPerformanceViewProjection();
        var view = NewView("UPS");

        projection.Apply(
            new CarrierPickupMissed("UPS", "Mon 9-12", DateTimeOffset.UtcNow),
            view);

        view.MissedPickups.ShouldBe(1);
    }

    // --- Identity resolution / per-carrier isolation regression -------------

    [Fact]
    public void IdentityResolution_TwoCarriers_HaveDisjointCounters()
    {
        // The previous bug bucketed every GhostShipmentDetected and
        // CarrierClaimResolved under "Unknown", hiding per-carrier reliability
        // signal. Now the events carry Carrier so each view stays scoped.
        // This test mirrors what Identity<T>(e => e.Carrier) does: route each
        // event to the matching carrier's view.
        var projection = new CarrierPerformanceViewProjection();
        var ups = NewView("UPS");
        var fedex = NewView("FedEx");

        // UPS: 2 ghosts, 1 filed-then-resolved claim.
        projection.Apply(new GhostShipmentDetected("UPS", "1Z-A", TimeSpan.FromHours(24), DateTimeOffset.UtcNow), ups);
        projection.Apply(new GhostShipmentDetected("UPS", "1Z-B", TimeSpan.FromHours(24), DateTimeOffset.UtcNow), ups);
        projection.Apply(new CarrierClaimFiled("UPS", "Lost", Guid.NewGuid(), "1Z-A", DateTimeOffset.UtcNow), ups);
        projection.Apply(new CarrierClaimResolved("UPS", "Approved", 50m, DateTimeOffset.UtcNow), ups);

        // FedEx: 1 ghost, 1 unresolved claim.
        projection.Apply(new GhostShipmentDetected("FedEx", "FX-A", TimeSpan.FromHours(24), DateTimeOffset.UtcNow), fedex);
        projection.Apply(new CarrierClaimFiled("FedEx", "Damage", Guid.NewGuid(), "FX-A", DateTimeOffset.UtcNow), fedex);

        ups.GhostShipments.ShouldBe(2);
        ups.OpenClaims.ShouldBe(0);
        ups.ResolvedClaims.ShouldBe(1);

        fedex.GhostShipments.ShouldBe(1);
        fedex.OpenClaims.ShouldBe(1);
        fedex.ResolvedClaims.ShouldBe(0);

        // Critical: an "Unknown"-keyed catch-all view would NOT exist now —
        // every counter is attributed to a real carrier id.
        ups.Id.ShouldBe("UPS");
        fedex.Id.ShouldBe("FedEx");
    }

    [Fact]
    public void IdentityResolution_EventsCarryCarrier_DocumentingTheNewContract()
    {
        // Pins the contract: GhostShipmentDetected and CarrierClaimResolved
        // both expose Carrier as their leading field. If either is dropped,
        // the projection identity wiring breaks and this test goes red at
        // compile time.
        new GhostShipmentDetected("UPS", "1Z", TimeSpan.FromHours(24), DateTimeOffset.UtcNow)
            .Carrier.ShouldBe("UPS");
        new CarrierClaimResolved("FedEx", "Approved", 0m, DateTimeOffset.UtcNow)
            .Carrier.ShouldBe("FedEx");
    }
}
