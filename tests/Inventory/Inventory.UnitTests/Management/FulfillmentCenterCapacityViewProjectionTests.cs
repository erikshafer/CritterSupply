namespace Inventory.UnitTests.Management;

/// <summary>
/// Pure function tests for <see cref="FulfillmentCenterCapacityViewProjection"/> Apply methods.
/// Slice 39 (P3): FC capacity data exposure.
/// </summary>
public class FulfillmentCenterCapacityViewProjectionTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private readonly FulfillmentCenterCapacityViewProjection _projection = new();

    private static FulfillmentCenterCapacityView EmptyView() => new();

    [Fact]
    public void InventoryInitialized_Increments_SkuCount_And_Available()
    {
        var view = EmptyView();

        _projection.Apply(view, new InventoryInitialized("DOG-FOOD-001", "NJ-FC", 100, Now));

        view.Id.ShouldBe("NJ-FC");
        view.SkuCount.ShouldBe(1);
        view.TotalAvailable.ShouldBe(100);
        view.TotalOnHand.ShouldBe(100);
    }

    [Fact]
    public void Multiple_SKUs_Same_Warehouse_Accumulate()
    {
        var view = EmptyView();

        _projection.Apply(view, new InventoryInitialized("DOG-FOOD-001", "NJ-FC", 100, Now));
        _projection.Apply(view, new InventoryInitialized("CAT-TOY-001", "NJ-FC", 50, Now));
        _projection.Apply(view, new InventoryInitialized("BIRD-SEED-001", "NJ-FC", 200, Now));

        view.SkuCount.ShouldBe(3);
        view.TotalAvailable.ShouldBe(350);
        view.TotalOnHand.ShouldBe(350);
    }

    [Fact]
    public void Reserve_Moves_Available_To_Reserved()
    {
        var view = InitializedView("NJ-FC", 100);

        _projection.Apply(view, new StockReserved(Guid.NewGuid(), Guid.NewGuid(), "SKU-01", "NJ-FC", 30, Now));

        view.TotalAvailable.ShouldBe(70);
        view.TotalReserved.ShouldBe(30);
        view.TotalOnHand.ShouldBe(100); // unchanged
    }

    [Fact]
    public void Pick_Moves_Committed_To_Picked()
    {
        var view = InitializedView("NJ-FC", 100);
        view.TotalAvailable = 70;
        view.TotalCommitted = 30;

        _projection.Apply(view, new StockPicked("SKU-01", "NJ-FC", Guid.NewGuid(), 30, Now));

        view.TotalCommitted.ShouldBe(0);
        view.TotalPicked.ShouldBe(30);
        view.TotalOnHand.ShouldBe(100);
    }

    [Fact]
    public void Ship_Removes_From_Picked()
    {
        var view = InitializedView("NJ-FC", 100);
        view.TotalAvailable = 70;
        view.TotalPicked = 30;

        _projection.Apply(view, new StockShipped("SKU-01", "NJ-FC", Guid.NewGuid(), 30, Guid.NewGuid(), Now));

        view.TotalPicked.ShouldBe(0);
        view.TotalOnHand.ShouldBe(70);
    }

    [Fact]
    public void TransferOut_Decrements_Available_And_Tracks_InTransit()
    {
        var view = InitializedView("NJ-FC", 100);

        _projection.Apply(view, new StockTransferredOut("SKU-01", "NJ-FC", Guid.NewGuid(), 25, Now));

        view.TotalAvailable.ShouldBe(75);
        view.TotalInTransitOut.ShouldBe(25);
        view.TotalOnHand.ShouldBe(75);
    }

    [Fact]
    public void TransferIn_Increments_Available()
    {
        var view = InitializedView("OH-FC", 50);

        _projection.Apply(view, new StockTransferredIn("SKU-01", "OH-FC", Guid.NewGuid(), 25, Now));

        view.TotalAvailable.ShouldBe(75);
        view.TotalOnHand.ShouldBe(75);
    }

    [Fact]
    public void Quarantine_Tracks_Quarantined_Bucket()
    {
        var view = InitializedView("NJ-FC", 100);

        _projection.Apply(view, new StockQuarantined("SKU-01", "NJ-FC", Guid.NewGuid(), 20, "Suspect batch", "clerk", Now));
        // Available decrement happens via companion InventoryAdjusted
        _projection.Apply(view, new InventoryAdjusted("SKU-01", "NJ-FC", -20, "Quarantine", "clerk", Now));

        view.TotalQuarantined.ShouldBe(20);
        view.TotalAvailable.ShouldBe(80);
    }

    [Fact]
    public void Quarantine_Release_Decrements_Quarantined()
    {
        var view = InitializedView("NJ-FC", 100);
        view.TotalQuarantined = 20;
        view.TotalAvailable = 80;

        _projection.Apply(view, new QuarantineReleased("SKU-01", "NJ-FC", Guid.NewGuid(), 20, "clerk", Now));
        _projection.Apply(view, new InventoryAdjusted("SKU-01", "NJ-FC", 20, "Release", "clerk", Now));

        view.TotalQuarantined.ShouldBe(0);
        view.TotalAvailable.ShouldBe(100);
    }

    [Fact]
    public void StockReceived_Increments_Available()
    {
        var view = InitializedView("NJ-FC", 100);

        _projection.Apply(view, new StockReceived("SKU-01", "NJ-FC", "Supplier-A", null, 50, Now));

        view.TotalAvailable.ShouldBe(150);
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private FulfillmentCenterCapacityView InitializedView(string warehouseId, int quantity)
    {
        var view = EmptyView();
        _projection.Apply(view, new InventoryInitialized("SKU-01", warehouseId, quantity, Now));
        return view;
    }
}
