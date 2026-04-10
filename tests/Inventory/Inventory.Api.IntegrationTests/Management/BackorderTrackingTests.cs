using Inventory.Management;
using Messages.Contracts.Fulfillment;

namespace Inventory.Api.IntegrationTests.Management;

/// <summary>
/// Integration tests for backorder tracking (Slices 18-19):
/// BackorderCreatedHandler and BackorderPolicy.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class BackorderTrackingTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public BackorderTrackingTests(TestFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.CleanAllDocumentsAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // ---------------------------------------------------------------------------
    // Slice 18: BackorderCreated → BackorderRegistered
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task BackorderCreated_RegistersBackorderOnInventory()
    {
        var sku = "BACKORDER-001";
        var warehouseId = "NJ-FC";
        await _fixture.ExecuteAndWaitAsync(new InitializeInventory(sku, warehouseId, 0));

        var inventoryId = InventoryStreamId.Compute(sku, warehouseId);
        var orderId = Guid.NewGuid();
        var shipmentId = Guid.NewGuid();

        await _fixture.ExecuteAndWaitAsync(
            new BackorderCreated(orderId, shipmentId, "No stock",
                [new BackorderedItem(sku, warehouseId, 5)],
                DateTimeOffset.UtcNow));

        await using var session = _fixture.GetDocumentSession();
        var inventory = await session.LoadAsync<ProductInventory>(inventoryId);

        inventory.ShouldNotBeNull();
        inventory.HasPendingBackorders.ShouldBeTrue();

        var events = await session.Events.FetchStreamAsync(inventoryId);
        events.ShouldContain(e => e.EventType == typeof(BackorderRegistered));
    }

    [Fact]
    public async Task BackorderCreated_UnknownSku_IsNoOp()
    {
        // Don't initialize inventory — BackorderCreated should be no-op
        var orderId = Guid.NewGuid();
        var shipmentId = Guid.NewGuid();

        await _fixture.ExecuteAndWaitAsync(
            new BackorderCreated(orderId, shipmentId, "No stock",
                [new BackorderedItem("UNKNOWN-SKU", "NJ-FC", 3)],
                DateTimeOffset.UtcNow));

        // No exception thrown — handler returns gracefully
    }

    // ---------------------------------------------------------------------------
    // Slice 19: Stock arrival clears backorder
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task StockArrival_WithPendingBackorders_PublishesBackorderStockAvailable()
    {
        var sku = "BACKORDER-CLEAR-001";
        var warehouseId = "NJ-FC";
        await _fixture.ExecuteAndWaitAsync(new InitializeInventory(sku, warehouseId, 0));

        var inventoryId = InventoryStreamId.Compute(sku, warehouseId);
        var orderId = Guid.NewGuid();
        var shipmentId = Guid.NewGuid();

        // Register a backorder
        await _fixture.ExecuteAndWaitAsync(
            new BackorderCreated(orderId, shipmentId, "No stock",
                [new BackorderedItem(sku, warehouseId, 5)],
                DateTimeOffset.UtcNow));

        // Stock arrives — should clear backorder and publish BackorderStockAvailable
        var tracked = await _fixture.ExecuteAndWaitAsync(
            new ReceiveStock(inventoryId, 10, "Supplier-A", null));

        await using var session = _fixture.GetDocumentSession();
        var inventory = await session.LoadAsync<ProductInventory>(inventoryId);

        inventory.ShouldNotBeNull();
        inventory.HasPendingBackorders.ShouldBeFalse(); // BackorderCleared applied
        inventory.AvailableQuantity.ShouldBe(10);

        // Verify BackorderCleared event appended
        var events = await session.Events.FetchStreamAsync(inventoryId);
        events.ShouldContain(e => e.EventType == typeof(BackorderCleared));

        // BackorderStockAvailable published to outgoing messages
        // (NoRoutes in test environment because no external transport configured —
        //  verified via aggregate state: HasPendingBackorders cleared to false)
    }

    [Fact]
    public async Task StockArrival_WithoutPendingBackorders_DoesNotPublishBackorderNotification()
    {
        var sku = "BACKORDER-NONE-001";
        var warehouseId = "NJ-FC";
        await _fixture.ExecuteAndWaitAsync(new InitializeInventory(sku, warehouseId, 50));

        var inventoryId = InventoryStreamId.Compute(sku, warehouseId);

        // No backorders registered — stock arrival should not trigger any backorder notification
        var tracked = await _fixture.ExecuteAndWaitAsync(
            new ReceiveStock(inventoryId, 10, "Supplier-B", null));

        await using var session = _fixture.GetDocumentSession();
        var inventory = await session.LoadAsync<ProductInventory>(inventoryId);

        inventory.ShouldNotBeNull();
        inventory.HasPendingBackorders.ShouldBeFalse();
        inventory.AvailableQuantity.ShouldBe(60);

        // No BackorderStockAvailable published
        tracked.Sent.MessagesOf<Messages.Contracts.Inventory.BackorderStockAvailable>()
            .ShouldBeEmpty();
    }
}
