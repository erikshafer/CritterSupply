using Inventory.Management;
using Shouldly;

namespace Inventory.UnitTests.Management;

/// <summary>
/// Unit tests for ProductInventory Apply methods added in S3
/// (transfer events, quarantine events, replenishment).
/// </summary>
public class ProductInventoryS3ApplyTests
{
    private static readonly DateTimeOffset DefaultTimestamp = DateTimeOffset.UtcNow;
    private const string DefaultSku = "SKU-001";
    private const string DefaultWarehouse = "WH-01";

    private static ProductInventory CreateInventory(int available = 100, int quarantined = 0)
    {
        var evt = new InventoryInitialized(DefaultSku, DefaultWarehouse, available, DefaultTimestamp);
        var inv = ProductInventory.Create(evt);
        if (quarantined > 0)
        {
            inv = inv with { QuarantinedQuantity = quarantined };
        }
        return inv;
    }

    // --- Transfer events ---

    [Fact]
    public void Apply_StockTransferredOut_Decrements_AvailableQuantity()
    {
        var inv = CreateInventory(100);
        var result = inv.Apply(new StockTransferredOut(DefaultSku, DefaultWarehouse, Guid.NewGuid(), 30, DefaultTimestamp));
        result.AvailableQuantity.ShouldBe(70);
    }

    [Fact]
    public void Apply_StockTransferredIn_Increments_AvailableQuantity()
    {
        var inv = CreateInventory(100);
        var result = inv.Apply(new StockTransferredIn(DefaultSku, DefaultWarehouse, Guid.NewGuid(), 25, DefaultTimestamp));
        result.AvailableQuantity.ShouldBe(125);
    }

    // --- Quarantine events ---

    [Fact]
    public void Apply_StockQuarantined_Increments_QuarantinedQuantity()
    {
        var inv = CreateInventory(100);
        var quarantineId = Guid.NewGuid();
        var result = inv.Apply(new StockQuarantined(DefaultSku, DefaultWarehouse, quarantineId, 15, "Suspect quality", "clerk", DefaultTimestamp));
        result.QuarantinedQuantity.ShouldBe(15);
    }

    [Fact]
    public void Apply_QuarantineReleased_Decrements_QuarantinedQuantity()
    {
        var inv = CreateInventory(100, quarantined: 20);
        var quarantineId = Guid.NewGuid();
        var result = inv.Apply(new QuarantineReleased(DefaultSku, DefaultWarehouse, quarantineId, 10, "clerk", DefaultTimestamp));
        result.QuarantinedQuantity.ShouldBe(10);
    }

    [Fact]
    public void Apply_QuarantineDisposed_Decrements_QuarantinedQuantity()
    {
        var inv = CreateInventory(100, quarantined: 20);
        var quarantineId = Guid.NewGuid();
        var result = inv.Apply(new QuarantineDisposed(DefaultSku, DefaultWarehouse, quarantineId, 20, "Contaminated", "ops", DefaultTimestamp));
        result.QuarantinedQuantity.ShouldBe(0);
    }

    // --- Replenishment (no-op on aggregate) ---

    [Fact]
    public void Apply_ReplenishmentTriggered_Is_NoOp()
    {
        var inv = CreateInventory(5);
        var result = inv.Apply(new ReplenishmentTriggered(DefaultSku, DefaultWarehouse, 5, 10, true, DefaultTimestamp));
        result.AvailableQuantity.ShouldBe(5);
        result.QuarantinedQuantity.ShouldBe(0);
    }

    // --- Quarantine round-trip integration ---

    [Fact]
    public void Quarantine_And_Release_Restores_QuarantinedQuantity()
    {
        var inv = CreateInventory(100);
        var qId = Guid.NewGuid();

        // Quarantine
        inv = inv.Apply(new StockQuarantined(DefaultSku, DefaultWarehouse, qId, 30, "reason", "clerk", DefaultTimestamp));
        // InventoryAdjusted (which would also fire in the handler) decrements available
        inv = inv.Apply(new InventoryAdjusted(DefaultSku, DefaultWarehouse, -30, "Quarantine", "clerk", DefaultTimestamp));
        inv.AvailableQuantity.ShouldBe(70);
        inv.QuarantinedQuantity.ShouldBe(30);

        // Release
        inv = inv.Apply(new QuarantineReleased(DefaultSku, DefaultWarehouse, qId, 30, "clerk", DefaultTimestamp));
        inv = inv.Apply(new InventoryAdjusted(DefaultSku, DefaultWarehouse, 30, "Release", "clerk", DefaultTimestamp));
        inv.AvailableQuantity.ShouldBe(100);
        inv.QuarantinedQuantity.ShouldBe(0);
    }

    [Fact]
    public void Quarantine_And_Dispose_Removes_Permanently()
    {
        var inv = CreateInventory(100);
        var qId = Guid.NewGuid();

        // Quarantine
        inv = inv.Apply(new StockQuarantined(DefaultSku, DefaultWarehouse, qId, 20, "reason", "clerk", DefaultTimestamp));
        inv = inv.Apply(new InventoryAdjusted(DefaultSku, DefaultWarehouse, -20, "Quarantine", "clerk", DefaultTimestamp));
        inv.QuarantinedQuantity.ShouldBe(20);
        inv.AvailableQuantity.ShouldBe(80);

        // Dispose (StockWrittenOff is no-op on aggregate, QuarantineDisposed decrements quarantine)
        inv = inv.Apply(new QuarantineDisposed(DefaultSku, DefaultWarehouse, qId, 20, "Contaminated", "ops", DefaultTimestamp));
        inv.QuarantinedQuantity.ShouldBe(0);
        // Available stays at 80 — the write-off doesn't affect available (it was already removed during quarantine)
        inv.AvailableQuantity.ShouldBe(80);
    }
}
