using Inventory.Api.InventoryManagement;
using Inventory.Management;

namespace Inventory.Api.IntegrationTests.Commands;

/// <summary>
/// Integration tests for POST /api/inventory/{sku}/adjust endpoint.
/// Tests the AdjustInventory write operation added in M32.1 Session 3.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class AdjustInventoryEndpointTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public AdjustInventoryEndpointTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.CleanAllDocumentsAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task AdjustInventory_PositiveAdjustment_IncreasesQuantity()
    {
        // Arrange: Product with initial inventory
        await using var session = _fixture.GetDocumentSession();
        var sku = "CAT-TOY-001";
        var warehouseId = "main";
        var inventoryId = ProductInventory.CombinedGuid(sku, warehouseId);

        session.Events.StartStream<ProductInventory>(
            inventoryId,
            new InventoryInitialized(sku, warehouseId, 0, DateTimeOffset.UtcNow.AddDays(-1)),
            new StockReceived(50, "Initial stock", DateTimeOffset.UtcNow.AddDays(-1)));

        await session.SaveChangesAsync();

        // Act: Adjust inventory up
        var result = await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new AdjustInventoryRequest(
                AdjustmentQuantity: 25,
                Reason: "Found extra boxes in warehouse",
                AdjustedBy: "warehouse.manager@crittersupply.com")).ToUrl($"/api/inventory/{sku}/adjust");
            x.StatusCodeShouldBeOk();
        });

        // Assert: Verify quantity increased
        var response = result.ReadAsJson<AdjustInventoryResult>();
        response.ShouldNotBeNull();
        response.Sku.ShouldBe(sku);
        response.NewAvailableQuantity.ShouldBe(75); // 50 + 25
    }

    [Fact]
    public async Task AdjustInventory_NegativeAdjustment_DecreasesQuantity()
    {
        // Arrange: Product with initial inventory
        await using var session = _fixture.GetDocumentSession();
        var sku = "DOG-TREAT-002";
        var warehouseId = "main";
        var inventoryId = ProductInventory.CombinedGuid(sku, warehouseId);

        session.Events.StartStream<ProductInventory>(
            inventoryId,
            new InventoryInitialized(sku, warehouseId, 0, DateTimeOffset.UtcNow.AddDays(-1)),
            new StockReceived(100, "Initial stock", DateTimeOffset.UtcNow.AddDays(-1)));

        await session.SaveChangesAsync();

        // Act: Adjust inventory down (damage write-off)
        var result = await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new AdjustInventoryRequest(
                AdjustmentQuantity: -15,
                Reason: "Damaged items - water leak",
                AdjustedBy: "warehouse.clerk@crittersupply.com")).ToUrl($"/api/inventory/{sku}/adjust");
            x.StatusCodeShouldBeOk();
        });

        // Assert: Verify quantity decreased
        var response = result.ReadAsJson<AdjustInventoryResult>();
        response.ShouldNotBeNull();
        response.Sku.ShouldBe(sku);
        response.NewAvailableQuantity.ShouldBe(85); // 100 - 15
    }

    [Fact]
    public async Task AdjustInventory_NonExistentSku_ReturnsNotFound()
    {
        // Act: Try to adjust inventory for non-existent SKU
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new AdjustInventoryRequest(
                AdjustmentQuantity: 10,
                Reason: "Test adjustment",
                AdjustedBy: "test@example.com")).ToUrl("/api/inventory/DOES-NOT-EXIST/adjust");
            x.StatusCodeShouldBe(404);
        });
    }

    [Fact]
    public async Task AdjustInventory_NegativeAdjustmentBelowZero_ReturnsBadRequest()
    {
        // Arrange: Product with low inventory
        await using var session = _fixture.GetDocumentSession();
        var sku = "BIRD-SEED-003";
        var warehouseId = "main";
        var inventoryId = ProductInventory.CombinedGuid(sku, warehouseId);

        session.Events.StartStream<ProductInventory>(
            inventoryId,
            new InventoryInitialized(sku, warehouseId, 0, DateTimeOffset.UtcNow.AddDays(-1)),
            new StockReceived(10, "Initial stock", DateTimeOffset.UtcNow.AddDays(-1)));

        await session.SaveChangesAsync();

        // Act: Try to adjust down more than available
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new AdjustInventoryRequest(
                AdjustmentQuantity: -20,
                Reason: "Over-adjustment attempt",
                AdjustedBy: "test@example.com")).ToUrl($"/api/inventory/{sku}/adjust");
            x.StatusCodeShouldBe(400);
        });
    }

    [Fact]
    public async Task AdjustInventory_MultipleAdjustments_AccumulatesChanges()
    {
        // Arrange: Product with initial inventory
        await using var session = _fixture.GetDocumentSession();
        var sku = "FISH-FOOD-004";
        var warehouseId = "main";
        var inventoryId = ProductInventory.CombinedGuid(sku, warehouseId);

        session.Events.StartStream<ProductInventory>(
            inventoryId,
            new InventoryInitialized(sku, warehouseId, 0, DateTimeOffset.UtcNow.AddDays(-1)),
            new StockReceived(100, "Initial stock", DateTimeOffset.UtcNow.AddDays(-1)));

        await session.SaveChangesAsync();

        // Act: Multiple adjustments
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new AdjustInventoryRequest(
                AdjustmentQuantity: 20,
                Reason: "Found extra inventory",
                AdjustedBy: "clerk1@example.com")).ToUrl($"/api/inventory/{sku}/adjust");
            x.StatusCodeShouldBeOk();
        });

        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new AdjustInventoryRequest(
                AdjustmentQuantity: -10,
                Reason: "Damaged items",
                AdjustedBy: "clerk2@example.com")).ToUrl($"/api/inventory/{sku}/adjust");
            x.StatusCodeShouldBeOk();
        });

        var result = await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new AdjustInventoryRequest(
                AdjustmentQuantity: 5,
                Reason: "Final count correction",
                AdjustedBy: "clerk3@example.com")).ToUrl($"/api/inventory/{sku}/adjust");
            x.StatusCodeShouldBeOk();
        });

        // Assert: Verify net adjustment (100 + 20 - 10 + 5 = 115)
        var response = result.ReadAsJson<AdjustInventoryResult>();
        response.ShouldNotBeNull();
        response.NewAvailableQuantity.ShouldBe(115);
    }
}
