using Fulfillment.Shipments;
using Shouldly;

namespace Fulfillment.UnitTests.Shipments;

/// <summary>
/// Unit tests for P1 failure mode Apply() methods on the Shipment aggregate.
/// </summary>
public class ShipmentFailureModeTests
{
    private static readonly ShippingAddress DefaultAddress = new(
        "123 Main St", null, "Springfield", "IL", "62701", "US");

    private static Shipment BuildAssignedShipment() =>
        Shipment.Create(new FulfillmentRequested(
            Guid.NewGuid(), Guid.NewGuid(), DefaultAddress,
            [new FulfillmentLineItem("CAT-FOOD-001", 2)],
            "Standard", DateTimeOffset.UtcNow))
        .Apply(new FulfillmentCenterAssigned("NJ-FC", DateTimeOffset.UtcNow));

    private static Shipment BuildLabeledShipment() =>
        BuildAssignedShipment()
            .Apply(new ShippingLabelGenerated("UPS", "Ground", 10m, null, DateTimeOffset.UtcNow))
            .Apply(new TrackingNumberAssigned("1Z999AA1", "UPS", DateTimeOffset.UtcNow));

    [Fact]
    public void Apply_ShipmentRerouted_Resets_To_Assigned()
    {
        var shipment = BuildAssignedShipment()
            .Apply(new ShipmentRerouted("NJ-FC", "OH-FC", DateTimeOffset.UtcNow));
        shipment.Status.ShouldBe(ShipmentStatus.Assigned);
        shipment.AssignedFulfillmentCenter.ShouldBe("OH-FC");
    }

    [Fact]
    public void Apply_BackorderCreated_Sets_Backordered()
    {
        var shipment = BuildAssignedShipment()
            .Apply(new BackorderCreated("No stock", DateTimeOffset.UtcNow));
        shipment.Status.ShouldBe(ShipmentStatus.Backordered);
    }

    [Fact]
    public void Apply_ShippingLabelGenerationFailed_Sets_LabelGenerationFailed()
    {
        var shipment = BuildAssignedShipment()
            .Apply(new ShippingLabelGenerationFailed("UPS", "API timeout", DateTimeOffset.UtcNow));
        shipment.Status.ShouldBe(ShipmentStatus.LabelGenerationFailed);
    }

    [Fact]
    public void Apply_AlternateCarrierArranged_Updates_Carrier()
    {
        var shipment = BuildLabeledShipment()
            .Apply(new AlternateCarrierArranged("UPS", "FedEx", DateTimeOffset.UtcNow));
        shipment.Carrier.ShouldBe("FedEx");
    }

    [Fact]
    public void Apply_GhostShipmentDetected_Sets_Investigation()
    {
        var shipment = BuildAssignedShipment()
            .Apply(new ShipmentHandedToCarrier("UPS", "1Z", DateTimeOffset.UtcNow))
            .Apply(new GhostShipmentDetected("1Z", TimeSpan.FromHours(25), DateTimeOffset.UtcNow));
        shipment.Status.ShouldBe(ShipmentStatus.GhostShipmentInvestigation);
    }

    [Fact]
    public void Apply_ShipmentInTransit_Resolves_GhostInvestigation()
    {
        var shipment = BuildAssignedShipment()
            .Apply(new ShipmentHandedToCarrier("UPS", "1Z", DateTimeOffset.UtcNow))
            .Apply(new GhostShipmentDetected("1Z", TimeSpan.FromHours(25), DateTimeOffset.UtcNow))
            .Apply(new ShipmentInTransit("Hub", "Edison, NJ", DateTimeOffset.UtcNow));
        shipment.Status.ShouldBe(ShipmentStatus.InTransit);
    }

    [Fact]
    public void Apply_ShipmentLostInTransit_Sets_LostInTransit()
    {
        var shipment = BuildAssignedShipment()
            .Apply(new ShipmentHandedToCarrier("UPS", "1Z", DateTimeOffset.UtcNow))
            .Apply(new ShipmentLostInTransit("UPS", TimeSpan.FromDays(7), DateTimeOffset.UtcNow));
        shipment.Status.ShouldBe(ShipmentStatus.LostInTransit);
    }

    [Fact]
    public void Apply_ReturnReceivedAtWarehouse_Sets_ReturnReceived()
    {
        var shipment = BuildAssignedShipment()
            .Apply(new ReturnToSenderInitiated("UPS", 3, 7, DateTimeOffset.UtcNow))
            .Apply(new ReturnReceivedAtWarehouse(DateTimeOffset.UtcNow, "NJ-FC"));
        shipment.Status.ShouldBe(ShipmentStatus.ReturnReceived);
    }
}
