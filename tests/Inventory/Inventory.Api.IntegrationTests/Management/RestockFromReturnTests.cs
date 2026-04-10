using Inventory.Management;
using Messages.Contracts.Returns;

namespace Inventory.Api.IntegrationTests.Management;

/// <summary>
/// Integration test for RestockFromReturnHandler (S1 deferred).
/// Tests direct message invocation — no cross-BC fixture needed.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class RestockFromReturnTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public RestockFromReturnTests(TestFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.CleanAllDocumentsAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task RestockFromReturn_WhenReturnPasses_AppendsStockRestocked()
    {
        var sku = "DOG-FOOD-40LB";
        var warehouseId = "NJ-FC";
        await _fixture.ExecuteAndWaitAsync(new InitializeInventory(sku, warehouseId, 100));
        var inventoryId = InventoryStreamId.Compute(sku, warehouseId);

        // Invoke the Returns integration event directly
        await _fixture.ExecuteAndWaitAsync(
            new ReturnCompleted(
                ReturnId: Guid.NewGuid(),
                OrderId: Guid.NewGuid(),
                CustomerId: Guid.NewGuid(),
                FinalRefundAmount: 45.99m,
                Items:
                [
                    new ReturnedItem(sku, 2, IsRestockable: true, warehouseId, "LikeNew", 45.99m, null)
                ],
                CompletedAt: DateTimeOffset.UtcNow));

        // Assert StockRestocked appended, AvailableQuantity increased
        await using var session = _fixture.GetDocumentSession();
        var inventory = await session.LoadAsync<ProductInventory>(inventoryId);

        inventory.ShouldNotBeNull();
        inventory.AvailableQuantity.ShouldBe(102); // 100 + 2 restocked

        var events = await session.Events.FetchStreamAsync(inventoryId);
        events.ShouldContain(e => e.EventType == typeof(StockRestocked));
    }

    [Fact]
    public async Task RestockFromReturn_NonRestockableItem_IsSkipped()
    {
        var sku = "CAT-TOY-001";
        var warehouseId = "NJ-FC";
        await _fixture.ExecuteAndWaitAsync(new InitializeInventory(sku, warehouseId, 50));
        var inventoryId = InventoryStreamId.Compute(sku, warehouseId);

        await _fixture.ExecuteAndWaitAsync(
            new ReturnCompleted(
                ReturnId: Guid.NewGuid(),
                OrderId: Guid.NewGuid(),
                CustomerId: Guid.NewGuid(),
                FinalRefundAmount: 12.99m,
                Items:
                [
                    new ReturnedItem(sku, 1, IsRestockable: false, warehouseId, null, 12.99m, "Damaged beyond repair")
                ],
                CompletedAt: DateTimeOffset.UtcNow));

        await using var session = _fixture.GetDocumentSession();
        var inventory = await session.LoadAsync<ProductInventory>(inventoryId);

        inventory.ShouldNotBeNull();
        inventory.AvailableQuantity.ShouldBe(50); // unchanged
    }

    [Fact]
    public async Task RestockFromReturn_UnknownSku_IsSkipped()
    {
        // Don't initialize inventory for this SKU
        await _fixture.ExecuteAndWaitAsync(
            new ReturnCompleted(
                ReturnId: Guid.NewGuid(),
                OrderId: Guid.NewGuid(),
                CustomerId: Guid.NewGuid(),
                FinalRefundAmount: 99.99m,
                Items:
                [
                    new ReturnedItem("UNKNOWN-SKU", 3, IsRestockable: true, "NJ-FC", "New", 99.99m, null)
                ],
                CompletedAt: DateTimeOffset.UtcNow));

        // No exception — handler skips unknown inventory
    }

    [Fact]
    public async Task RestockFromReturn_MultipleItems_RestocksEachEligible()
    {
        var sku1 = "MULTI-RESTOCK-001";
        var sku2 = "MULTI-RESTOCK-002";
        var warehouseId = "NJ-FC";
        await _fixture.ExecuteAndWaitAsync(new InitializeInventory(sku1, warehouseId, 40));
        await _fixture.ExecuteAndWaitAsync(new InitializeInventory(sku2, warehouseId, 60));

        await _fixture.ExecuteAndWaitAsync(
            new ReturnCompleted(
                ReturnId: Guid.NewGuid(),
                OrderId: Guid.NewGuid(),
                CustomerId: Guid.NewGuid(),
                FinalRefundAmount: 150.00m,
                Items:
                [
                    new ReturnedItem(sku1, 3, IsRestockable: true, warehouseId, "New", 75.00m, null),
                    new ReturnedItem(sku2, 1, IsRestockable: true, warehouseId, "LikeNew", 75.00m, null)
                ],
                CompletedAt: DateTimeOffset.UtcNow));

        await using var session = _fixture.GetDocumentSession();
        var inv1 = await session.LoadAsync<ProductInventory>(InventoryStreamId.Compute(sku1, warehouseId));
        var inv2 = await session.LoadAsync<ProductInventory>(InventoryStreamId.Compute(sku2, warehouseId));

        inv1!.AvailableQuantity.ShouldBe(43);
        inv2!.AvailableQuantity.ShouldBe(61);
    }
}
