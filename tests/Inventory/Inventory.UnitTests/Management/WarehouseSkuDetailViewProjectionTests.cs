namespace Inventory.UnitTests.Management;

/// <summary>
/// Pure function tests for <see cref="WarehouseSkuDetailViewProjection"/> Apply methods.
/// Covers all ProductInventory events including S3 additions: transfers and quarantine.
/// </summary>
public class WarehouseSkuDetailViewProjectionTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private readonly WarehouseSkuDetailViewProjection _projection = new();

    private static WarehouseSkuDetailView EmptyView() => new();

    // ---------------------------------------------------------------------------
    // P0 — Foundation events
    // ---------------------------------------------------------------------------

    [Fact]
    public void InventoryInitialized_Sets_Identity_And_Available()
    {
        var view = EmptyView();

        _projection.Apply(view, new InventoryInitialized("DOG-FOOD-001", "WH-NJ", 200, Now));

        view.Id.ShouldBe(InventoryStreamId.Compute("DOG-FOOD-001", "WH-NJ"));
        view.Sku.ShouldBe("DOG-FOOD-001");
        view.WarehouseId.ShouldBe("WH-NJ");
        view.AvailableQuantity.ShouldBe(200);
        view.TotalOnHand.ShouldBe(200);
        view.LastUpdated.ShouldBe(Now);
    }

    [Fact]
    public void StockReserved_Moves_Available_To_Reserved()
    {
        var view = InitializedView("SKU-01", "WH-01", 100);

        _projection.Apply(view, new StockReserved(Guid.NewGuid(), Guid.NewGuid(), "SKU-01", "WH-01", 25, Now));

        view.AvailableQuantity.ShouldBe(75);
        view.ReservedQuantity.ShouldBe(25);
        view.TotalOnHand.ShouldBe(100); // unchanged
    }

    [Fact]
    public void ReservationReleased_Restores_Available_From_Reserved()
    {
        var view = InitializedView("SKU-01", "WH-01", 100);
        _projection.Apply(view, new StockReserved(Guid.NewGuid(), Guid.NewGuid(), "SKU-01", "WH-01", 30, Now));

        _projection.Apply(view, new ReservationReleased(Guid.NewGuid(), "SKU-01", "WH-01", 30, "cancelled", Now));

        view.AvailableQuantity.ShouldBe(100);
        view.ReservedQuantity.ShouldBe(0);
        view.TotalOnHand.ShouldBe(100);
    }

    [Fact]
    public void StockReceived_Increments_Available()
    {
        var view = InitializedView("SKU-01", "WH-01", 50);

        _projection.Apply(view, new StockReceived("SKU-01", "WH-01", "Supplier-A", null, 30, Now));

        view.AvailableQuantity.ShouldBe(80);
        view.TotalOnHand.ShouldBe(80);
    }

    [Fact]
    public void StockRestocked_Increments_Available()
    {
        var view = InitializedView("SKU-01", "WH-01", 50);

        _projection.Apply(view, new StockRestocked("SKU-01", "WH-01", Guid.NewGuid(), 20, Now));

        view.AvailableQuantity.ShouldBe(70);
    }

    [Fact]
    public void InventoryAdjusted_Adds_Or_Subtracts()
    {
        var view = InitializedView("SKU-01", "WH-01", 100);

        _projection.Apply(view, new InventoryAdjusted("SKU-01", "WH-01", -15, "damage", "clerk", Now));

        view.AvailableQuantity.ShouldBe(85);
    }

    // ---------------------------------------------------------------------------
    // P1 — Physical operations
    // ---------------------------------------------------------------------------

    [Fact]
    public void StockPicked_Moves_Committed_To_Picked()
    {
        var view = InitializedView("SKU-01", "WH-01", 100);
        // Simulate reserve + commit: 25 reserved, then committed
        view.ReservedQuantity = 0;
        view.CommittedQuantity = 25;
        view.AvailableQuantity = 75;

        _projection.Apply(view, new StockPicked("SKU-01", "WH-01", Guid.NewGuid(), 25, Now));

        view.CommittedQuantity.ShouldBe(0);
        view.PickedQuantity.ShouldBe(25);
        view.TotalOnHand.ShouldBe(100); // unchanged
    }

    [Fact]
    public void StockShipped_Removes_From_Picked_Decrements_TotalOnHand()
    {
        var view = InitializedView("SKU-01", "WH-01", 100);
        view.AvailableQuantity = 75;
        view.PickedQuantity = 25;

        _projection.Apply(view, new StockShipped("SKU-01", "WH-01", Guid.NewGuid(), 25, Guid.NewGuid(), Now));

        view.PickedQuantity.ShouldBe(0);
        view.TotalOnHand.ShouldBe(75); // 25 left the building
    }

    [Fact]
    public void ReservationExpired_Restores_Available_From_Reserved()
    {
        var view = InitializedView("SKU-01", "WH-01", 100);
        _projection.Apply(view, new StockReserved(Guid.NewGuid(), Guid.NewGuid(), "SKU-01", "WH-01", 20, Now));

        _projection.Apply(view, new ReservationExpired(Guid.NewGuid(), "SKU-01", "WH-01", 20, "Expired", Now));

        view.AvailableQuantity.ShouldBe(100);
        view.ReservedQuantity.ShouldBe(0);
    }

    // ---------------------------------------------------------------------------
    // P2 — Transfer events (S3 carryover)
    // ---------------------------------------------------------------------------

    [Fact]
    public void StockTransferredOut_Decrements_Available_Increments_InTransit()
    {
        var view = InitializedView("SKU-01", "WH-SRC", 100);

        _projection.Apply(view, new StockTransferredOut("SKU-01", "WH-SRC", Guid.NewGuid(), 30, Now));

        view.AvailableQuantity.ShouldBe(70);
        view.InTransitOutQuantity.ShouldBe(30);
        view.TotalOnHand.ShouldBe(70); // excludes in-transit
    }

    [Fact]
    public void StockTransferredIn_Increments_Available()
    {
        var view = InitializedView("SKU-01", "WH-DST", 50);

        _projection.Apply(view, new StockTransferredIn("SKU-01", "WH-DST", Guid.NewGuid(), 30, Now));

        view.AvailableQuantity.ShouldBe(80);
        view.TotalOnHand.ShouldBe(80);
    }

    [Fact]
    public void TransferLifecycle_Source_Shows_InTransit_Then_No_Change_On_Receive()
    {
        // Source warehouse: 100 available → transfer out 30 → 70 available, 30 in-transit
        var source = InitializedView("SKU-01", "WH-SRC", 100);

        _projection.Apply(source, new StockTransferredOut("SKU-01", "WH-SRC", Guid.NewGuid(), 30, Now));

        source.AvailableQuantity.ShouldBe(70);
        source.InTransitOutQuantity.ShouldBe(30);
        source.TotalOnHand.ShouldBe(70);

        // Destination warehouse: 50 available → transfer in 30 → 80 available
        var dest = InitializedView("SKU-01", "WH-DST", 50);

        _projection.Apply(dest, new StockTransferredIn("SKU-01", "WH-DST", Guid.NewGuid(), 30, Now));

        dest.AvailableQuantity.ShouldBe(80);
        dest.TotalOnHand.ShouldBe(80);
    }

    // ---------------------------------------------------------------------------
    // P2 — Quarantine events (S3 carryover)
    // ---------------------------------------------------------------------------

    [Fact]
    public void StockQuarantined_Increments_Quarantined_Bucket()
    {
        var view = InitializedView("SKU-01", "WH-01", 100);

        _projection.Apply(view, new StockQuarantined("SKU-01", "WH-01", Guid.NewGuid(), 20, "Suspect batch", "clerk", Now));

        view.QuarantinedQuantity.ShouldBe(20);
        // Note: Available is decremented by companion InventoryAdjusted, not by StockQuarantined
    }

    [Fact]
    public void Quarantine_Release_Restores_Available_RoundTrip()
    {
        var view = InitializedView("SKU-01", "WH-01", 100);
        var quarantineId = Guid.NewGuid();

        // Quarantine 20: StockQuarantined + InventoryAdjusted(-20)
        _projection.Apply(view, new StockQuarantined("SKU-01", "WH-01", quarantineId, 20, "Suspect", "clerk", Now));
        _projection.Apply(view, new InventoryAdjusted("SKU-01", "WH-01", -20, "Quarantine", "clerk", Now));

        view.AvailableQuantity.ShouldBe(80);
        view.QuarantinedQuantity.ShouldBe(20);
        view.TotalOnHand.ShouldBe(80);

        // Release 20: QuarantineReleased + InventoryAdjusted(+20)
        _projection.Apply(view, new QuarantineReleased("SKU-01", "WH-01", quarantineId, 20, "clerk", Now));
        _projection.Apply(view, new InventoryAdjusted("SKU-01", "WH-01", 20, "Release", "clerk", Now));

        view.AvailableQuantity.ShouldBe(100);
        view.QuarantinedQuantity.ShouldBe(0);
        view.TotalOnHand.ShouldBe(100);
    }

    [Fact]
    public void Quarantine_Dispose_Permanently_Removes_Stock()
    {
        var view = InitializedView("SKU-01", "WH-01", 100);
        var quarantineId = Guid.NewGuid();

        // Quarantine 25: StockQuarantined + InventoryAdjusted(-25)
        _projection.Apply(view, new StockQuarantined("SKU-01", "WH-01", quarantineId, 25, "Contaminated", "clerk", Now));
        _projection.Apply(view, new InventoryAdjusted("SKU-01", "WH-01", -25, "Quarantine", "clerk", Now));

        view.AvailableQuantity.ShouldBe(75);
        view.QuarantinedQuantity.ShouldBe(25);

        // Dispose 25: QuarantineDisposed (no InventoryAdjusted since already removed from available)
        _projection.Apply(view, new QuarantineDisposed("SKU-01", "WH-01", quarantineId, 25, "Health hazard", "ops", Now));

        view.QuarantinedQuantity.ShouldBe(0);
        view.AvailableQuantity.ShouldBe(75); // stays at 75
        view.TotalOnHand.ShouldBe(75); // permanently reduced
    }

    // ---------------------------------------------------------------------------
    // Composite scenario: full lifecycle
    // ---------------------------------------------------------------------------

    [Fact]
    public void FullLifecycle_Initialize_Reserve_Commit_Pick_Ship_Matches_Aggregate()
    {
        var view = InitializedView("SKU-01", "WH-01", 100);
        var reservationId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        // Reserve 10
        _projection.Apply(view, new StockReserved(orderId, reservationId, "SKU-01", "WH-01", 10, Now));
        view.AvailableQuantity.ShouldBe(90);
        view.ReservedQuantity.ShouldBe(10);
        view.TotalOnHand.ShouldBe(100);

        // Commit is a no-op in the view (doesn't carry quantity)
        _projection.Apply(view, new ReservationCommitted(reservationId, "SKU-01", "WH-01", Now));
        // Reserved → Committed: view tracks timestamp only (aggregate handles bucket transfer)
        view.TotalOnHand.ShouldBe(100);

        // Pick 10 (Committed → Picked)
        _projection.Apply(view, new StockPicked("SKU-01", "WH-01", reservationId, 10, Now));
        view.PickedQuantity.ShouldBe(10);
        view.TotalOnHand.ShouldBe(100); // still in building

        // Ship 10 (Picked → gone)
        _projection.Apply(view, new StockShipped("SKU-01", "WH-01", reservationId, 10, Guid.NewGuid(), Now));
        view.PickedQuantity.ShouldBe(0);
        view.TotalOnHand.ShouldBe(90); // left the building
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private WarehouseSkuDetailView InitializedView(string sku, string warehouseId, int quantity)
    {
        var view = EmptyView();
        _projection.Apply(view, new InventoryInitialized(sku, warehouseId, quantity, Now));
        return view;
    }
}
