using Inventory.Management;

namespace Inventory.Api.IntegrationTests.Management;

/// <summary>
/// Integration tests for cycle count workflow (Slices 20-22),
/// damage recording (Slice 23), and stock write-off (Slice 24).
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class PhysicalOperationsTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public PhysicalOperationsTests(TestFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.CleanAllDocumentsAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // ---------------------------------------------------------------------------
    // Slices 20-22: Cycle Count
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task CycleCount_NoDiscrepancy_OnlyCycleCountEvents()
    {
        var sku = "CYCLE-001";
        var warehouseId = "NJ-FC";
        await _fixture.ExecuteAndWaitAsync(new InitializeInventory(sku, warehouseId, 100));
        var inventoryId = InventoryStreamId.Compute(sku, warehouseId);

        // Initiate
        await _fixture.ExecuteAndWaitAsync(new InitiateCycleCount(sku, warehouseId, "clerk-a"));

        // Complete — physical count matches system count
        await _fixture.ExecuteAndWaitAsync(new CompleteCycleCount(sku, warehouseId, 100, "clerk-a"));

        await using var session = _fixture.GetDocumentSession();
        var inventory = await session.LoadAsync<ProductInventory>(inventoryId);

        inventory.ShouldNotBeNull();
        inventory.AvailableQuantity.ShouldBe(100); // unchanged

        var events = await session.Events.FetchStreamAsync(inventoryId);
        events.ShouldContain(e => e.EventType == typeof(CycleCountInitiated));
        events.ShouldContain(e => e.EventType == typeof(CycleCountCompleted));
        events.ShouldNotContain(e => e.EventType == typeof(StockDiscrepancyFound));
        events.ShouldNotContain(e => e.EventType == typeof(InventoryAdjusted));
    }

    [Fact]
    public async Task CycleCount_Shortage_AppendsDiscrepancyAndAdjustment()
    {
        var sku = "CYCLE-SHORT-001";
        var warehouseId = "NJ-FC";
        await _fixture.ExecuteAndWaitAsync(new InitializeInventory(sku, warehouseId, 100));
        var inventoryId = InventoryStreamId.Compute(sku, warehouseId);

        // Physical count is less than system — 5 items missing
        await _fixture.ExecuteAndWaitAsync(new CompleteCycleCount(sku, warehouseId, 95, "clerk-b"));

        await using var session = _fixture.GetDocumentSession();
        var inventory = await session.LoadAsync<ProductInventory>(inventoryId);

        inventory.ShouldNotBeNull();
        inventory.AvailableQuantity.ShouldBe(95); // adjusted down by 5

        var events = await session.Events.FetchStreamAsync(inventoryId);
        events.ShouldContain(e => e.EventType == typeof(StockDiscrepancyFound));
        events.ShouldContain(e => e.EventType == typeof(InventoryAdjusted));
    }

    [Fact]
    public async Task CycleCount_Surplus_AppendsDiscrepancyAndPositiveAdjustment()
    {
        var sku = "CYCLE-SURPLUS-001";
        var warehouseId = "NJ-FC";
        await _fixture.ExecuteAndWaitAsync(new InitializeInventory(sku, warehouseId, 100));
        var inventoryId = InventoryStreamId.Compute(sku, warehouseId);

        // Physical count is more than system — 10 extra items found
        await _fixture.ExecuteAndWaitAsync(new CompleteCycleCount(sku, warehouseId, 110, "clerk-c"));

        await using var session = _fixture.GetDocumentSession();
        var inventory = await session.LoadAsync<ProductInventory>(inventoryId);

        inventory.ShouldNotBeNull();
        inventory.AvailableQuantity.ShouldBe(110); // adjusted up by 10
    }

    [Fact]
    public async Task CycleCount_WouldGoNegative_RejectedWithProblemDetails()
    {
        var sku = "CYCLE-NEG-001";
        var warehouseId = "NJ-FC";
        await _fixture.ExecuteAndWaitAsync(new InitializeInventory(sku, warehouseId, 100));
        var inventoryId = InventoryStreamId.Compute(sku, warehouseId);

        var orderId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();

        // Reserve 80, leaving 20 available
        await _fixture.ExecuteAndWaitAsync(new ReserveStock(orderId, sku, warehouseId, reservationId, 80));

        // Physical count of 10 would result in available = 10 - 80 = -70 — reject
        await _fixture.ExecuteAndWaitAsync(new CompleteCycleCount(sku, warehouseId, 10, "clerk-d"));

        // Verify inventory unchanged (ProblemDetails returned, no events appended)
        await using var session = _fixture.GetDocumentSession();
        var inventory = await session.LoadAsync<ProductInventory>(inventoryId);

        inventory.ShouldNotBeNull();
        inventory.AvailableQuantity.ShouldBe(20); // unchanged from reservation
    }

    [Fact]
    public async Task CycleCount_WithReservations_AdjustsOnlyAvailable()
    {
        var sku = "CYCLE-RES-001";
        var warehouseId = "NJ-FC";
        await _fixture.ExecuteAndWaitAsync(new InitializeInventory(sku, warehouseId, 100));
        var inventoryId = InventoryStreamId.Compute(sku, warehouseId);

        var orderId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();

        // Reserve 30 → available = 70, reserved = 30, total = 100
        await _fixture.ExecuteAndWaitAsync(new ReserveStock(orderId, sku, warehouseId, reservationId, 30));

        // Physical count = 95 → expected available = 95 - 30 = 65 (shortage of 5)
        await _fixture.ExecuteAndWaitAsync(new CompleteCycleCount(sku, warehouseId, 95, "clerk-e"));

        await using var session = _fixture.GetDocumentSession();
        var inventory = await session.LoadAsync<ProductInventory>(inventoryId);

        inventory.ShouldNotBeNull();
        inventory.AvailableQuantity.ShouldBe(65); // 95 - 30 reserved
        inventory.ReservedQuantity.ShouldBe(30); // unchanged
        inventory.TotalOnHand.ShouldBe(95);
    }

    // ---------------------------------------------------------------------------
    // Slice 23: Record Damage
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task RecordDamage_DecrementsAvailableQuantity()
    {
        var sku = "DAMAGE-001";
        var warehouseId = "NJ-FC";
        await _fixture.ExecuteAndWaitAsync(new InitializeInventory(sku, warehouseId, 100));
        var inventoryId = InventoryStreamId.Compute(sku, warehouseId);

        await _fixture.ExecuteAndWaitAsync(new RecordDamage(inventoryId, 5, "Water damage", "clerk-f"));

        await using var session = _fixture.GetDocumentSession();
        var inventory = await session.LoadAsync<ProductInventory>(inventoryId);

        inventory.ShouldNotBeNull();
        inventory.AvailableQuantity.ShouldBe(95);

        var events = await session.Events.FetchStreamAsync(inventoryId);
        events.ShouldContain(e => e.EventType == typeof(DamageRecorded));
        events.ShouldContain(e => e.EventType == typeof(InventoryAdjusted));
    }

    [Fact]
    public async Task RecordDamage_ExceedsAvailable_Rejected()
    {
        var sku = "DAMAGE-REJECT-001";
        var warehouseId = "NJ-FC";
        await _fixture.ExecuteAndWaitAsync(new InitializeInventory(sku, warehouseId, 3));
        var inventoryId = InventoryStreamId.Compute(sku, warehouseId);

        // Try to record damage of 10 when only 3 available
        await _fixture.ExecuteAndWaitAsync(new RecordDamage(inventoryId, 10, "Fire damage", "clerk-g"));

        await using var session = _fixture.GetDocumentSession();
        var inventory = await session.LoadAsync<ProductInventory>(inventoryId);

        inventory.ShouldNotBeNull();
        inventory.AvailableQuantity.ShouldBe(3); // unchanged
    }

    // ---------------------------------------------------------------------------
    // Slice 24: Stock Write-Off
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task WriteOffStock_DecrementsAvailableQuantity()
    {
        var sku = "WRITEOFF-001";
        var warehouseId = "NJ-FC";
        await _fixture.ExecuteAndWaitAsync(new InitializeInventory(sku, warehouseId, 50));
        var inventoryId = InventoryStreamId.Compute(sku, warehouseId);

        await _fixture.ExecuteAndWaitAsync(new WriteOffStock(inventoryId, 20, "Regulatory recall", "ops-mgr"));

        await using var session = _fixture.GetDocumentSession();
        var inventory = await session.LoadAsync<ProductInventory>(inventoryId);

        inventory.ShouldNotBeNull();
        inventory.AvailableQuantity.ShouldBe(30);

        var events = await session.Events.FetchStreamAsync(inventoryId);
        events.ShouldContain(e => e.EventType == typeof(StockWrittenOff));
        events.ShouldContain(e => e.EventType == typeof(InventoryAdjusted));
    }

    [Fact]
    public async Task WriteOffStock_ExceedsAvailable_Rejected()
    {
        var sku = "WRITEOFF-REJECT-001";
        var warehouseId = "NJ-FC";
        await _fixture.ExecuteAndWaitAsync(new InitializeInventory(sku, warehouseId, 5));
        var inventoryId = InventoryStreamId.Compute(sku, warehouseId);

        await _fixture.ExecuteAndWaitAsync(new WriteOffStock(inventoryId, 50, "Complete disposal", "ops-mgr"));

        await using var session = _fixture.GetDocumentSession();
        var inventory = await session.LoadAsync<ProductInventory>(inventoryId);

        inventory.ShouldNotBeNull();
        inventory.AvailableQuantity.ShouldBe(5); // unchanged
    }
}
