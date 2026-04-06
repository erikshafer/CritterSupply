using System.Collections.Immutable;
using Fulfillment.WorkOrders;
using Shouldly;

namespace Fulfillment.UnitTests.Shipments;

/// <summary>
/// Unit tests for WorkOrder aggregate Create and Apply methods.
/// </summary>
public class WorkOrderTests
{
    private static WorkOrder BuildCreatedWorkOrder()
    {
        var evt = new WorkOrderCreated(
            WorkOrderId: Guid.NewGuid(),
            ShipmentId: Guid.NewGuid(),
            FulfillmentCenterId: "NJ-FC",
            LineItems: [new WorkOrderLineItem("DOG-FOOD-40LB", 1), new WorkOrderLineItem("CAT-TOY", 2)],
            CreatedAt: DateTimeOffset.UtcNow);
        return WorkOrder.Create(evt);
    }

    [Fact]
    public void Create_Sets_Created_Status()
    {
        var wo = BuildCreatedWorkOrder();
        wo.Status.ShouldBe(WorkOrderStatus.Created);
        wo.FulfillmentCenterId.ShouldBe("NJ-FC");
        wo.LineItems.Count.ShouldBe(2);
    }

    [Fact]
    public void Apply_WaveReleased_Sets_Status()
    {
        var wo = BuildCreatedWorkOrder()
            .Apply(new WaveReleased("WAVE-001", DateTimeOffset.UtcNow));
        wo.Status.ShouldBe(WorkOrderStatus.WaveReleased);
        wo.WaveReleasedAt.ShouldNotBeNull();
    }

    [Fact]
    public void Apply_PickListAssigned_Sets_Picker()
    {
        var wo = BuildCreatedWorkOrder()
            .Apply(new WaveReleased("W", DateTimeOffset.UtcNow))
            .Apply(new PickListAssigned("P-Smith", DateTimeOffset.UtcNow));
        wo.Status.ShouldBe(WorkOrderStatus.PickListAssigned);
        wo.AssignedPicker.ShouldBe("P-Smith");
    }

    [Fact]
    public void Apply_ItemPicked_Tracks_Quantities()
    {
        var wo = BuildCreatedWorkOrder()
            .Apply(new ItemPicked("DOG-FOOD-40LB", 1, "A-01", "P-1", DateTimeOffset.UtcNow));
        wo.PickedQuantities["DOG-FOOD-40LB"].ShouldBe(1);
    }

    [Fact]
    public void AllItemsPicked_True_When_All_Picked()
    {
        var wo = BuildCreatedWorkOrder()
            .Apply(new ItemPicked("DOG-FOOD-40LB", 1, "A-01", "P-1", DateTimeOffset.UtcNow))
            .Apply(new ItemPicked("CAT-TOY", 2, "B-01", "P-1", DateTimeOffset.UtcNow));
        wo.AllItemsPicked.ShouldBeTrue();
    }

    [Fact]
    public void AllItemsPicked_False_When_Partial()
    {
        var wo = BuildCreatedWorkOrder()
            .Apply(new ItemPicked("DOG-FOOD-40LB", 1, "A-01", "P-1", DateTimeOffset.UtcNow));
        wo.AllItemsPicked.ShouldBeFalse();
    }

    [Fact]
    public void Apply_ItemVerifiedAtPack_Tracks_Quantities()
    {
        var wo = BuildCreatedWorkOrder()
            .Apply(new ItemVerifiedAtPack("DOG-FOOD-40LB", 1, DateTimeOffset.UtcNow));
        wo.VerifiedQuantities["DOG-FOOD-40LB"].ShouldBe(1);
    }

    [Fact]
    public void AllItemsVerified_True_When_All_Verified()
    {
        var wo = BuildCreatedWorkOrder()
            .Apply(new ItemVerifiedAtPack("DOG-FOOD-40LB", 1, DateTimeOffset.UtcNow))
            .Apply(new ItemVerifiedAtPack("CAT-TOY", 2, DateTimeOffset.UtcNow));
        wo.AllItemsVerified.ShouldBeTrue();
    }

    [Fact]
    public void Apply_PackingCompleted_Sets_Final_State()
    {
        var wo = BuildCreatedWorkOrder()
            .Apply(new PackingCompleted(12.5m, "Medium", DateTimeOffset.UtcNow));
        wo.Status.ShouldBe(WorkOrderStatus.PackingCompleted);
        wo.BillableWeightLbs.ShouldBe(12.5m);
        wo.CartonSize.ShouldBe("Medium");
    }

    [Fact]
    public void StreamId_Is_Deterministic()
    {
        var shipmentId = Guid.NewGuid();
        var id1 = WorkOrder.StreamId(shipmentId, "NJ-FC");
        var id2 = WorkOrder.StreamId(shipmentId, "NJ-FC");
        id1.ShouldBe(id2);
    }

    [Fact]
    public void StreamId_Different_FCs_Produce_Different_Ids()
    {
        var shipmentId = Guid.NewGuid();
        var id1 = WorkOrder.StreamId(shipmentId, "NJ-FC");
        var id2 = WorkOrder.StreamId(shipmentId, "OH-FC");
        id1.ShouldNotBe(id2);
    }
}
