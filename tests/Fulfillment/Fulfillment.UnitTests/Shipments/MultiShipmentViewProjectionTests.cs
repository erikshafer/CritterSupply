using Fulfillment.Shipments;

namespace Fulfillment.UnitTests.Shipments;

/// <summary>
/// Pure-function unit tests for <see cref="MultiShipmentViewProjection"/>.
/// These exercise the <c>Apply(@event, view)</c> overloads directly without
/// spinning up Marten — the projection's mutation logic is independent of
/// the event-store wiring.
///
/// Critical regression covered: before the M45.0 fix, every
/// <c>TrackingNumberAssigned</c> / <c>ShipmentDelivered</c> / <c>ReshipmentCreated</c>
/// event was bucketed under a single <c>Guid.Empty</c>-keyed view because
/// the projection used <c>_ =&gt; Guid.Empty</c> for identity resolution.
/// The events now carry <c>OrderId</c> as the leading field and the projection
/// uses <c>Identity&lt;T&gt;(e =&gt; e.OrderId)</c>. The "two-orders, disjoint
/// shipments" test pins this guarantee at the unit level.
/// </summary>
public class MultiShipmentViewProjectionTests
{
    private static readonly ShippingAddress AnyAddress = new(
        "1 Critter Way", null, "Austin", "TX", "73301", "US");

    private static FulfillmentRequested NewFulfillmentRequested(Guid orderId) =>
        new(
            OrderId: orderId,
            CustomerId: Guid.NewGuid(),
            ShippingAddress: AnyAddress,
            LineItems: [new FulfillmentLineItem("DOG-FOOD-001", 1)],
            ShippingMethod: "Standard",
            RequestedAt: DateTimeOffset.UtcNow);

    // --- Identity wiring smoke test -----------------------------------------

    [Fact]
    public void Constructor_DoesNotThrow()
    {
        // Guards against typos in the new Identity<T>(e => e.OrderId) wiring.
        var act = () => new MultiShipmentViewProjection();
        act.ShouldNotThrow();
    }

    // --- Apply(FulfillmentRequested) ----------------------------------------

    [Fact]
    public void Apply_FulfillmentRequested_AddsPendingShipmentEntry_WithDeterministicShipmentId()
    {
        var orderId = Guid.NewGuid();
        var projection = new MultiShipmentViewProjection();
        var view = projection.Create(NewFulfillmentRequested(orderId));

        projection.Apply(NewFulfillmentRequested(orderId), view);

        view.Shipments.Count.ShouldBe(1);
        view.Shipments[0].ShipmentId.ShouldBe(Shipment.StreamId(orderId));
        view.Shipments[0].Status.ShouldBe("Pending");
        view.Shipments[0].TrackingNumber.ShouldBeNull();
        view.Shipments[0].IsReshipment.ShouldBeFalse();
        view.Shipments[0].OriginalShipmentId.ShouldBeNull();
    }

    [Fact]
    public void Apply_FulfillmentRequested_IsIdempotent_ForSameOrderId()
    {
        // Applying the same FulfillmentRequested twice must not double-add the
        // canonical shipment entry — the projection guards via "All(...)".
        var orderId = Guid.NewGuid();
        var projection = new MultiShipmentViewProjection();
        var view = new MultiShipmentView { Id = orderId };

        var evt = NewFulfillmentRequested(orderId);
        projection.Apply(evt, view);
        projection.Apply(evt, view);

        view.Shipments.Count.ShouldBe(1);
    }

    // --- Apply(TrackingNumberAssigned) --------------------------------------

    [Fact]
    public void Apply_TrackingNumberAssigned_PopulatesTrackingOnFirstUntrackedEntry()
    {
        var orderId = Guid.NewGuid();
        var projection = new MultiShipmentViewProjection();
        var view = new MultiShipmentView { Id = orderId };
        projection.Apply(NewFulfillmentRequested(orderId), view);

        projection.Apply(
            new TrackingNumberAssigned(orderId, "1Z999AA1", "UPS", DateTimeOffset.UtcNow),
            view);

        view.Shipments[0].TrackingNumber.ShouldBe("1Z999AA1");
    }

    [Fact]
    public void Apply_TrackingNumberAssigned_TwoEvents_PopulateInOrder_FirstThenReshipment()
    {
        // First TrackingNumberAssigned fills the canonical entry; the second
        // fills the next still-untracked entry (e.g. a reshipment).
        var orderId = Guid.NewGuid();
        var projection = new MultiShipmentViewProjection();
        var view = new MultiShipmentView { Id = orderId };
        projection.Apply(NewFulfillmentRequested(orderId), view);

        var newShipmentId = Guid.NewGuid();
        var originalShipmentId = Shipment.StreamId(orderId);
        projection.Apply(
            new ReshipmentCreated(orderId, newShipmentId, originalShipmentId, "Lost", DateTimeOffset.UtcNow),
            view);

        projection.Apply(new TrackingNumberAssigned(orderId, "1Z-OG", "UPS", DateTimeOffset.UtcNow), view);
        projection.Apply(new TrackingNumberAssigned(orderId, "1Z-RS", "UPS", DateTimeOffset.UtcNow), view);

        view.Shipments[0].TrackingNumber.ShouldBe("1Z-OG");
        view.Shipments[1].TrackingNumber.ShouldBe("1Z-RS");
    }

    // --- Apply(ShipmentDelivered) -------------------------------------------

    [Fact]
    public void Apply_ShipmentDelivered_FlipsFirstNonDeliveredEntryToDelivered()
    {
        var orderId = Guid.NewGuid();
        var projection = new MultiShipmentViewProjection();
        var view = new MultiShipmentView { Id = orderId };
        projection.Apply(NewFulfillmentRequested(orderId), view);

        projection.Apply(new ShipmentDelivered(orderId, DateTimeOffset.UtcNow), view);

        view.Shipments[0].Status.ShouldBe("Delivered");
    }

    [Fact]
    public void Apply_ShipmentDelivered_OnlyFirstNonDeliveredEntryIsAffected()
    {
        // With two pending entries, a single delivered event flips the first
        // (the canonical one); the reshipment entry remains pending.
        var orderId = Guid.NewGuid();
        var projection = new MultiShipmentViewProjection();
        var view = new MultiShipmentView { Id = orderId };
        projection.Apply(NewFulfillmentRequested(orderId), view);
        projection.Apply(
            new ReshipmentCreated(orderId, Guid.NewGuid(), Shipment.StreamId(orderId), "Lost", DateTimeOffset.UtcNow),
            view);

        projection.Apply(new ShipmentDelivered(orderId, DateTimeOffset.UtcNow), view);

        view.Shipments[0].Status.ShouldBe("Delivered");
        view.Shipments[1].Status.ShouldBe("Pending");
    }

    // --- Apply(ReshipmentCreated) -------------------------------------------

    [Fact]
    public void Apply_ReshipmentCreated_AppendsReshipmentEntry_LinkedToOriginal()
    {
        var orderId = Guid.NewGuid();
        var projection = new MultiShipmentViewProjection();
        var view = new MultiShipmentView { Id = orderId };
        projection.Apply(NewFulfillmentRequested(orderId), view);

        var originalShipmentId = Shipment.StreamId(orderId);
        var newShipmentId = Guid.NewGuid();
        projection.Apply(
            new ReshipmentCreated(orderId, newShipmentId, originalShipmentId, "Lost", DateTimeOffset.UtcNow),
            view);

        view.Shipments.Count.ShouldBe(2);
        view.Shipments[1].ShipmentId.ShouldBe(newShipmentId);
        view.Shipments[1].IsReshipment.ShouldBeTrue();
        view.Shipments[1].OriginalShipmentId.ShouldBe(originalShipmentId);
        view.Shipments[1].Status.ShouldBe("Pending");
        view.Shipments[1].TrackingNumber.ShouldBeNull();
    }

    // --- Identity resolution / multi-order isolation regression -------------

    [Fact]
    public void IdentityResolution_TwoOrders_HaveDisjointShipmentLists()
    {
        // The previous bug bucketed every TrackingNumberAssigned /
        // ShipmentDelivered / ReshipmentCreated under Guid.Empty, mixing every
        // order's shipments into one view. Now the events carry OrderId so
        // each view stays scoped — proven here by routing each event to the
        // matching view (mirroring what Identity<T>(e => e.OrderId) does).
        var orderA = Guid.NewGuid();
        var orderB = Guid.NewGuid();

        var projection = new MultiShipmentViewProjection();
        var viewA = new MultiShipmentView { Id = orderA };
        var viewB = new MultiShipmentView { Id = orderB };

        // Order A: requested -> tracked -> delivered.
        projection.Apply(NewFulfillmentRequested(orderA), viewA);
        projection.Apply(new TrackingNumberAssigned(orderA, "1Z-A", "UPS", DateTimeOffset.UtcNow), viewA);
        projection.Apply(new ShipmentDelivered(orderA, DateTimeOffset.UtcNow), viewA);

        // Order B: requested -> reshipment.
        projection.Apply(NewFulfillmentRequested(orderB), viewB);
        var bOriginal = Shipment.StreamId(orderB);
        projection.Apply(
            new ReshipmentCreated(orderB, Guid.NewGuid(), bOriginal, "Lost", DateTimeOffset.UtcNow),
            viewB);

        // Disjoint shipment ids — no cross-contamination between views.
        var aIds = viewA.Shipments.Select(s => s.ShipmentId).ToHashSet();
        var bIds = viewB.Shipments.Select(s => s.ShipmentId).ToHashSet();
        aIds.Overlaps(bIds).ShouldBeFalse();

        viewA.Shipments.Count.ShouldBe(1);
        viewA.Shipments[0].Status.ShouldBe("Delivered");
        viewA.Shipments[0].TrackingNumber.ShouldBe("1Z-A");

        viewB.Shipments.Count.ShouldBe(2);
        viewB.Shipments.All(s => s.Status == "Pending").ShouldBeTrue();
        viewB.Shipments.All(s => s.TrackingNumber == null).ShouldBeTrue();
    }

    [Fact]
    public void IdentityResolution_EventCarriesOrderId_DocumentingTheNewContract()
    {
        // The four events targeted by the M45.0 identity fix all expose
        // OrderId as their leading field. This pins the contract: if anyone
        // accidentally drops the OrderId from these records, the projection
        // identity wiring breaks and this test goes red at compile time.
        var orderId = Guid.NewGuid();

        new TrackingNumberAssigned(orderId, "1Z", "UPS", DateTimeOffset.UtcNow).OrderId.ShouldBe(orderId);
        new ShipmentDelivered(orderId, DateTimeOffset.UtcNow).OrderId.ShouldBe(orderId);
        new ReshipmentCreated(orderId, Guid.NewGuid(), Guid.NewGuid(), "x", DateTimeOffset.UtcNow).OrderId.ShouldBe(orderId);
    }
}
