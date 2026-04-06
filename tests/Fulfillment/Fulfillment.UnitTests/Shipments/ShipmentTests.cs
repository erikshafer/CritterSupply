using Fulfillment.Shipments;
using Shouldly;

namespace Fulfillment.UnitTests.Shipments;

/// <summary>
/// Unit tests for Shipment.Create and all Apply() overloads.
/// Updated for the remastered Shipment aggregate (ADR 0059).
/// </summary>
public class ShipmentTests
{
    private static readonly ShippingAddress DefaultAddress = new(
        "123 Main St", null, "Springfield", "IL", "62701", "US");

    private static Shipment BuildPendingShipment(Guid? orderId = null) =>
        Shipment.Create(new FulfillmentRequested(
            OrderId: orderId ?? Guid.NewGuid(),
            CustomerId: Guid.NewGuid(),
            ShippingAddress: DefaultAddress,
            LineItems: [new FulfillmentLineItem("CAT-FOOD-001", 2)],
            ShippingMethod: "Standard",
            RequestedAt: DateTimeOffset.UtcNow));

    private static Shipment BuildAssignedShipment(string fc = "NJ-FC") =>
        BuildPendingShipment().Apply(new FulfillmentCenterAssigned(fc, DateTimeOffset.UtcNow));

    // --- Create ---

    [Fact]
    public void Create_Sets_Pending_Status()
    {
        var shipment = BuildPendingShipment();
        shipment.Status.ShouldBe(ShipmentStatus.Pending);
    }

    [Fact]
    public void Create_Sets_OrderId()
    {
        var orderId = Guid.NewGuid();
        var shipment = BuildPendingShipment(orderId);
        shipment.OrderId.ShouldBe(orderId);
    }

    [Fact]
    public void Create_Nullables_Are_Null()
    {
        var shipment = BuildPendingShipment();
        shipment.AssignedFulfillmentCenter.ShouldBeNull();
        shipment.TrackingNumber.ShouldBeNull();
        shipment.Carrier.ShouldBeNull();
        shipment.DeliveredAt.ShouldBeNull();
        shipment.DeliveryAttemptCount.ShouldBe(0);
    }

    // --- Apply(FulfillmentCenterAssigned) ---

    [Fact]
    public void Apply_FulfillmentCenterAssigned_Sets_Status()
    {
        var shipment = BuildPendingShipment()
            .Apply(new FulfillmentCenterAssigned("WA-FC", DateTimeOffset.UtcNow));
        shipment.Status.ShouldBe(ShipmentStatus.Assigned);
        shipment.AssignedFulfillmentCenter.ShouldBe("WA-FC");
    }

    // --- Apply(ShippingLabelGenerated) ---

    [Fact]
    public void Apply_ShippingLabelGenerated_Sets_Labeled_Status()
    {
        var shipment = BuildAssignedShipment()
            .Apply(new ShippingLabelGenerated("UPS", "Ground", 10m, null, DateTimeOffset.UtcNow));
        shipment.Status.ShouldBe(ShipmentStatus.Labeled);
        shipment.Carrier.ShouldBe("UPS");
    }

    // --- Apply(TrackingNumberAssigned) ---

    [Fact]
    public void Apply_TrackingNumberAssigned_Sets_TrackingNumber()
    {
        var shipment = BuildAssignedShipment()
            .Apply(new ShippingLabelGenerated("UPS", "Ground", 10m, null, DateTimeOffset.UtcNow))
            .Apply(new TrackingNumberAssigned("1Z999AA1", "UPS", DateTimeOffset.UtcNow));
        shipment.TrackingNumber.ShouldBe("1Z999AA1");
    }

    // --- Apply(ShipmentHandedToCarrier) ---

    [Fact]
    public void Apply_ShipmentHandedToCarrier_Sets_Status()
    {
        var shipment = BuildAssignedShipment()
            .Apply(new ShippingLabelGenerated("UPS", "Ground", 10m, null, DateTimeOffset.UtcNow))
            .Apply(new ShipmentHandedToCarrier("UPS", "1Z999AA1", DateTimeOffset.UtcNow));
        shipment.Status.ShouldBe(ShipmentStatus.HandedToCarrier);
        shipment.HandedToCarrierAt.ShouldNotBeNull();
    }

    // --- Apply(ShipmentInTransit) ---

    [Fact]
    public void Apply_ShipmentInTransit_Sets_Status_And_Location()
    {
        var shipment = BuildAssignedShipment()
            .Apply(new ShipmentHandedToCarrier("UPS", "1Z", DateTimeOffset.UtcNow))
            .Apply(new ShipmentInTransit("Hub scan", "Edison, NJ", DateTimeOffset.UtcNow));
        shipment.Status.ShouldBe(ShipmentStatus.InTransit);
        shipment.LastScanLocation.ShouldBe("Edison, NJ");
    }

    // --- Apply(ShipmentDelivered) ---

    [Fact]
    public void Apply_ShipmentDelivered_Sets_Terminal_Status()
    {
        var shipment = BuildAssignedShipment()
            .Apply(new ShipmentDelivered(DateTimeOffset.UtcNow, "John Doe"));
        shipment.Status.ShouldBe(ShipmentStatus.Delivered);
        shipment.IsTerminal.ShouldBeTrue();
    }

    // --- Apply(DeliveryAttemptFailed) ---

    [Fact]
    public void Apply_DeliveryAttemptFailed_Tracks_AttemptNumber()
    {
        var shipment = BuildAssignedShipment()
            .Apply(new DeliveryAttemptFailed(1, "NI", "No one home", DateTimeOffset.UtcNow));
        shipment.Status.ShouldBe(ShipmentStatus.DeliveryAttemptFailed);
        shipment.DeliveryAttemptCount.ShouldBe(1);
    }

    [Fact]
    public void Apply_DeliveryAttemptFailed_Three_Times_Counts_Correctly()
    {
        var shipment = BuildAssignedShipment()
            .Apply(new DeliveryAttemptFailed(1, "NI", "No one home", DateTimeOffset.UtcNow))
            .Apply(new DeliveryAttemptFailed(2, "NI", "No one home", DateTimeOffset.UtcNow))
            .Apply(new DeliveryAttemptFailed(3, "NI", "No one home", DateTimeOffset.UtcNow));
        shipment.DeliveryAttemptCount.ShouldBe(3);
    }

    // --- Apply(ReturnToSenderInitiated) ---

    [Fact]
    public void Apply_ReturnToSenderInitiated_Sets_Status()
    {
        var shipment = BuildAssignedShipment()
            .Apply(new ReturnToSenderInitiated("UPS", 3, 7, DateTimeOffset.UtcNow));
        shipment.Status.ShouldBe(ShipmentStatus.ReturningToSender);
    }

    // --- StreamId ---

    [Fact]
    public void StreamId_Is_Deterministic()
    {
        var orderId = Guid.NewGuid();
        var id1 = Shipment.StreamId(orderId);
        var id2 = Shipment.StreamId(orderId);
        id1.ShouldBe(id2);
    }

    [Fact]
    public void StreamId_Different_Orders_Produce_Different_Ids()
    {
        var id1 = Shipment.StreamId(Guid.NewGuid());
        var id2 = Shipment.StreamId(Guid.NewGuid());
        id1.ShouldNotBe(id2);
    }
}
