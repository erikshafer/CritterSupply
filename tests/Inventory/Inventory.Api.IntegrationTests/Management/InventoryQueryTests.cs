using Inventory.Management;
using Marten;

namespace Inventory.Api.IntegrationTests.Management;

/// <summary>
/// Integration tests for Inventory BC HTTP query endpoints.
/// Tests GET /api/inventory/{sku} and GET /api/inventory/low-stock endpoints.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class InventoryQueryTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public InventoryQueryTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.CleanAllDocumentsAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Test GET /api/inventory/{sku} returns stock level for existing SKU.
    /// </summary>
    [Fact]
    public async Task GetStockLevel_ExistingSku_ReturnsStockDetails()
    {
        // Arrange: Initialize inventory
        var sku = "SKU-QUERY-001";
        var warehouseId = "WH-01";
        var initialQuantity = 100;

        var initCommand = new InitializeInventory(sku, warehouseId, initialQuantity);
        await _fixture.ExecuteAndWaitAsync(initCommand);

        // Reserve some stock to verify all quantities are returned
        var reservationId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var reserveCommand = new ReserveStock(orderId, sku, warehouseId, reservationId, 25);
        await _fixture.ExecuteAndWaitAsync(reserveCommand);

        // Act: Query stock level via HTTP endpoint
        var result = await _fixture.Host.Scenario(x =>
        {
            x.Get.Url($"/api/inventory/{sku}");
            x.StatusCodeShouldBeOk();
        });

        // Assert: Verify response structure and values
        var response = result.ReadAsJson<Api.Queries.StockLevelResponse>();
        response.ShouldNotBeNull();
        response.Sku.ShouldBe(sku);
        response.WarehouseId.ShouldBe(warehouseId);
        response.AvailableQuantity.ShouldBe(75); // 100 - 25 reserved
        response.ReservedQuantity.ShouldBe(25);
        response.CommittedQuantity.ShouldBe(0);
        response.TotalOnHand.ShouldBe(100);
    }

    /// <summary>
    /// Test GET /api/inventory/{sku} returns 404 for nonexistent SKU.
    /// </summary>
    [Fact]
    public async Task GetStockLevel_NonexistentSku_ReturnsNotFound()
    {
        // Act: Query nonexistent SKU
        await _fixture.Host.Scenario(x =>
        {
            x.Get.Url("/api/inventory/DOES-NOT-EXIST");
            x.StatusCodeShouldBe(404);
        });
    }

    /// <summary>
    /// Test GET /api/inventory/{sku}?warehouseId=WH-01 supports warehouse query parameter.
    /// </summary>
    [Fact]
    public async Task GetStockLevel_WithWarehouseParameter_ReturnsCorrectWarehouse()
    {
        // Arrange: Initialize inventory for specific warehouse
        var sku = "SKU-QUERY-002";
        var warehouseId = "WH-01";
        var initialQuantity = 50;

        var initCommand = new InitializeInventory(sku, warehouseId, initialQuantity);
        await _fixture.ExecuteAndWaitAsync(initCommand);

        // Act: Query stock level with explicit warehouse parameter
        var result = await _fixture.Host.Scenario(x =>
        {
            x.Get.Url($"/api/inventory/{sku}?warehouseId={warehouseId}");
            x.StatusCodeShouldBeOk();
        });

        // Assert: Verify warehouse matches
        var response = result.ReadAsJson<Api.Queries.StockLevelResponse>();
        response.ShouldNotBeNull();
        response.WarehouseId.ShouldBe(warehouseId);
        response.AvailableQuantity.ShouldBe(initialQuantity);
    }

    /// <summary>
    /// Test GET /api/inventory/low-stock returns items below threshold.
    /// </summary>
    [Fact]
    public async Task GetLowStock_ReturnsItemsBelowThreshold()
    {
        // Arrange: Initialize multiple SKUs with varying stock levels
        var lowSku1 = "SKU-LOW-001";
        var lowSku2 = "SKU-LOW-002";
        var highSku = "SKU-HIGH-001";
        var warehouseId = "WH-01";

        // Create low stock items (< 10 default threshold)
        await _fixture.ExecuteAndWaitAsync(new InitializeInventory(lowSku1, warehouseId, 5));
        await _fixture.ExecuteAndWaitAsync(new InitializeInventory(lowSku2, warehouseId, 3));

        // Create high stock item (>= 10 default threshold)
        await _fixture.ExecuteAndWaitAsync(new InitializeInventory(highSku, warehouseId, 50));

        // Act: Query low stock items (default threshold = 10)
        var result = await _fixture.Host.Scenario(x =>
        {
            x.Get.Url("/api/inventory/low-stock");
            x.StatusCodeShouldBeOk();
        });

        // Assert: Verify only low stock items returned
        var response = result.ReadAsJson<Api.Queries.LowStockResponse>();
        response.ShouldNotBeNull();
        response.TotalLowStockItems.ShouldBe(2);
        response.Items.Count.ShouldBe(2);

        // Verify items are ordered by AvailableQuantity (ascending), then Sku
        response.Items[0].Sku.ShouldBe(lowSku2); // 3 units
        response.Items[0].AvailableQuantity.ShouldBe(3);
        response.Items[1].Sku.ShouldBe(lowSku1); // 5 units
        response.Items[1].AvailableQuantity.ShouldBe(5);

        // High stock item should NOT be included
        response.Items.ShouldNotContain(i => i.Sku == highSku);
    }

    /// <summary>
    /// Test GET /api/inventory/low-stock?threshold=20 supports custom threshold.
    /// </summary>
    [Fact]
    public async Task GetLowStock_WithCustomThreshold_ReturnsItemsBelowCustomThreshold()
    {
        // Arrange: Initialize SKUs at boundary of custom threshold (20)
        var belowSku = "SKU-BELOW-001";
        var atSku = "SKU-AT-001";
        var aboveSku = "SKU-ABOVE-001";
        var warehouseId = "WH-01";

        await _fixture.ExecuteAndWaitAsync(new InitializeInventory(belowSku, warehouseId, 15));
        await _fixture.ExecuteAndWaitAsync(new InitializeInventory(atSku, warehouseId, 20));
        await _fixture.ExecuteAndWaitAsync(new InitializeInventory(aboveSku, warehouseId, 25));

        // Act: Query low stock with threshold = 20
        var result = await _fixture.Host.Scenario(x =>
        {
            x.Get.Url("/api/inventory/low-stock?threshold=20");
            x.StatusCodeShouldBeOk();
        });

        // Assert: Only items < 20 returned
        var response = result.ReadAsJson<Api.Queries.LowStockResponse>();
        response.ShouldNotBeNull();
        response.TotalLowStockItems.ShouldBe(1);
        response.Items.Count.ShouldBe(1);
        response.Items[0].Sku.ShouldBe(belowSku);
        response.Items[0].AvailableQuantity.ShouldBe(15);
    }

    /// <summary>
    /// Test GET /api/inventory/low-stock returns empty list when no low stock items.
    /// </summary>
    [Fact]
    public async Task GetLowStock_NoLowStockItems_ReturnsEmptyList()
    {
        // Arrange: Initialize only high stock items
        var highSku = "SKU-HIGH-002";
        var warehouseId = "WH-01";

        await _fixture.ExecuteAndWaitAsync(new InitializeInventory(highSku, warehouseId, 100));

        // Act: Query low stock
        var result = await _fixture.Host.Scenario(x =>
        {
            x.Get.Url("/api/inventory/low-stock");
            x.StatusCodeShouldBeOk();
        });

        // Assert: Empty list
        var response = result.ReadAsJson<Api.Queries.LowStockResponse>();
        response.ShouldNotBeNull();
        response.TotalLowStockItems.ShouldBe(0);
        response.Items.Count.ShouldBe(0);
    }
}
