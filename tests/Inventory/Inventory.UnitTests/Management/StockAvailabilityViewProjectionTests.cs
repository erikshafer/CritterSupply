namespace Inventory.UnitTests.Management;

/// <summary>
/// Pure function tests for <see cref="StockAvailabilityViewProjection"/> Apply methods.
/// Verifies that each event correctly updates the <see cref="StockAvailabilityView"/> document.
/// </summary>
public class StockAvailabilityViewProjectionTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private readonly StockAvailabilityViewProjection _projection = new();

    private static StockAvailabilityView EmptyView() => new();

    [Fact]
    public void InventoryInitialized_Sets_Sku_And_Creates_First_Warehouse_Entry()
    {
        var view = EmptyView();

        _projection.Apply(view, new InventoryInitialized("CAT-FOOD-001", "NJ-FC", 100, Now));

        view.Sku.ShouldBe("CAT-FOOD-001");
        view.Id.ShouldBe("CAT-FOOD-001");
        view.Warehouses.Count.ShouldBe(1);
        view.Warehouses[0].WarehouseId.ShouldBe("NJ-FC");
        view.Warehouses[0].AvailableQuantity.ShouldBe(100);
        view.TotalAvailable.ShouldBe(100);
    }

    [Fact]
    public void Two_InventoryInitialized_For_Different_Warehouses_Creates_Two_Entries()
    {
        var view = EmptyView();

        _projection.Apply(view, new InventoryInitialized("CAT-FOOD-001", "NJ-FC", 100, Now));
        _projection.Apply(view, new InventoryInitialized("CAT-FOOD-001", "OH-FC", 50, Now));

        view.Warehouses.Count.ShouldBe(2);
        view.Warehouses.ShouldContain(w => w.WarehouseId == "NJ-FC" && w.AvailableQuantity == 100);
        view.Warehouses.ShouldContain(w => w.WarehouseId == "OH-FC" && w.AvailableQuantity == 50);
        view.TotalAvailable.ShouldBe(150);
    }

    [Fact]
    public void StockReserved_Decrements_Warehouse_Quantity()
    {
        var view = EmptyView();
        _projection.Apply(view, new InventoryInitialized("CAT-FOOD-001", "NJ-FC", 100, Now));

        _projection.Apply(view, new StockReserved(Guid.NewGuid(), Guid.NewGuid(), "CAT-FOOD-001", "NJ-FC", 25, Now));

        view.Warehouses[0].AvailableQuantity.ShouldBe(75);
        view.TotalAvailable.ShouldBe(75);
    }

    [Fact]
    public void ReservationReleased_Increments_Warehouse_Quantity()
    {
        var view = EmptyView();
        _projection.Apply(view, new InventoryInitialized("CAT-FOOD-001", "NJ-FC", 100, Now));
        _projection.Apply(view, new StockReserved(Guid.NewGuid(), Guid.NewGuid(), "CAT-FOOD-001", "NJ-FC", 30, Now));

        _projection.Apply(view, new ReservationReleased(Guid.NewGuid(), "CAT-FOOD-001", "NJ-FC", 30, "cancelled", Now));

        view.Warehouses[0].AvailableQuantity.ShouldBe(100);
        view.TotalAvailable.ShouldBe(100);
    }

    [Fact]
    public void StockReceived_Increments_Warehouse_Quantity()
    {
        var view = EmptyView();
        _projection.Apply(view, new InventoryInitialized("CAT-FOOD-001", "NJ-FC", 50, Now));

        _projection.Apply(view, new StockReceived("CAT-FOOD-001", "NJ-FC", "Supplier-A", null, 30, Now));

        view.Warehouses[0].AvailableQuantity.ShouldBe(80);
        view.TotalAvailable.ShouldBe(80);
    }

    [Fact]
    public void StockRestocked_Increments_Warehouse_Quantity()
    {
        var view = EmptyView();
        _projection.Apply(view, new InventoryInitialized("CAT-FOOD-001", "NJ-FC", 50, Now));

        _projection.Apply(view, new StockRestocked("CAT-FOOD-001", "NJ-FC", Guid.NewGuid(), 20, Now));

        view.Warehouses[0].AvailableQuantity.ShouldBe(70);
        view.TotalAvailable.ShouldBe(70);
    }

    [Fact]
    public void InventoryAdjusted_Adds_Or_Subtracts_Quantity()
    {
        var view = EmptyView();
        _projection.Apply(view, new InventoryInitialized("CAT-FOOD-001", "NJ-FC", 100, Now));

        // Positive adjustment
        _projection.Apply(view, new InventoryAdjusted("CAT-FOOD-001", "NJ-FC", 10, "found extras", "clerk", Now));
        view.Warehouses[0].AvailableQuantity.ShouldBe(110);

        // Negative adjustment
        _projection.Apply(view, new InventoryAdjusted("CAT-FOOD-001", "NJ-FC", -15, "damage write-off", "clerk", Now));
        view.Warehouses[0].AvailableQuantity.ShouldBe(95);
    }

    [Fact]
    public void ReservationCommitted_Does_Not_Change_Available_Quantity()
    {
        var view = EmptyView();
        _projection.Apply(view, new InventoryInitialized("CAT-FOOD-001", "NJ-FC", 100, Now));
        _projection.Apply(view, new StockReserved(Guid.NewGuid(), Guid.NewGuid(), "CAT-FOOD-001", "NJ-FC", 25, Now));

        var quantityBefore = view.Warehouses[0].AvailableQuantity;
        _projection.Apply(view, new ReservationCommitted(Guid.NewGuid(), "CAT-FOOD-001", "NJ-FC", Now));

        view.Warehouses[0].AvailableQuantity.ShouldBe(quantityBefore);
        view.TotalAvailable.ShouldBe(75);
    }

    [Fact]
    public void TotalAvailable_Sums_Across_Warehouses()
    {
        var view = EmptyView();
        _projection.Apply(view, new InventoryInitialized("CAT-FOOD-001", "NJ-FC", 100, Now));
        _projection.Apply(view, new InventoryInitialized("CAT-FOOD-001", "OH-FC", 50, Now));
        _projection.Apply(view, new InventoryInitialized("CAT-FOOD-001", "TX-FC", 75, Now));

        // Reserve from NJ-FC
        _projection.Apply(view, new StockReserved(Guid.NewGuid(), Guid.NewGuid(), "CAT-FOOD-001", "NJ-FC", 20, Now));

        view.Warehouses.Count.ShouldBe(3);
        view.TotalAvailable.ShouldBe(205); // (100-20) + 50 + 75
    }
}
