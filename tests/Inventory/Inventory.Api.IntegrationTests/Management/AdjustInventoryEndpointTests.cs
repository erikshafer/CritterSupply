using Inventory.Api.InventoryManagement;
using Inventory.Management;

namespace Inventory.Api.IntegrationTests.Management;

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
    public async Task AdjustInventory_PositiveAdjustment_IncreasesStock()
    {
        // Arrange: Initialize inventory
        var sku = "DOG-TOY-BALL";
        var warehouseId = "main";
        var initialQuantity = 50;

        var initCommand = new InitializeInventory(sku, warehouseId, initialQuantity);
        await _fixture.ExecuteAndWaitAsync(initCommand);

        // Act: Adjust inventory up by 20
        var result = await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new
            {
                adjustmentQuantity = 20,
                reason = "Received additional shipment from supplier",
                adjustedBy = "warehouse.clerk@crittersupply.com"
            }).ToUrl($"/api/inventory/{sku}/adjust");
            x.StatusCodeShouldBeOk();
        });

        // Assert: Verify new quantity
        var response = result.ReadAsJson<AdjustInventoryResult>();
        response.ShouldNotBeNull();
        response.Sku.ShouldBe(sku);
        response.WarehouseId.ShouldBe(warehouseId);
        response.NewAvailableQuantity.ShouldBe(70);  // 50 + 20
    }

    [Fact]
    public async Task AdjustInventory_NegativeAdjustment_DecreasesStock()
    {
        // Arrange: Initialize inventory
        var sku = "CAT-SCRATCHER";
        var warehouseId = "main";
        var initialQuantity = 100;

        var initCommand = new InitializeInventory(sku, warehouseId, initialQuantity);
        await _fixture.ExecuteAndWaitAsync(initCommand);

        // Act: Adjust inventory down by 10 (damaged items)
        var result = await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new
            {
                adjustmentQuantity = -10,
                reason = "Damaged during storage - written off",
                adjustedBy = "warehouse.manager@crittersupply.com"
            }).ToUrl($"/api/inventory/{sku}/adjust");
            x.StatusCodeShouldBeOk();
        });

        // Assert: Verify new quantity
        var response = result.ReadAsJson<AdjustInventoryResult>();
        response.ShouldNotBeNull();
        response.NewAvailableQuantity.ShouldBe(90);  // 100 - 10
    }

    [Fact]
    public async Task AdjustInventory_NegativeResultingInNegativeStock_ReturnsError()
    {
        // Arrange: Initialize inventory with low stock
        var sku = "HAMSTER-BEDDING";
        var warehouseId = "main";
        var initialQuantity = 5;

        var initCommand = new InitializeInventory(sku, warehouseId, initialQuantity);
        await _fixture.ExecuteAndWaitAsync(initCommand);

        // Act: Try to adjust down more than available
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new
            {
                adjustmentQuantity = -10,  // Would result in -5
                reason = "Cycle count discrepancy",
                adjustedBy = "warehouse.clerk@crittersupply.com"
            }).ToUrl($"/api/inventory/{sku}/adjust");
            x.StatusCodeShouldBe(400);
        });
    }

    [Fact]
    public async Task AdjustInventory_ZeroAdjustment_ReturnsValidationError()
    {
        // Arrange: Initialize inventory
        var sku = "FISH-FLAKES";
        var warehouseId = "main";
        var initialQuantity = 200;

        var initCommand = new InitializeInventory(sku, warehouseId, initialQuantity);
        await _fixture.ExecuteAndWaitAsync(initCommand);

        // Act: Try to adjust by zero
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new
            {
                adjustmentQuantity = 0,  // Invalid
                reason = "Testing",
                adjustedBy = "test@crittersupply.com"
            }).ToUrl($"/api/inventory/{sku}/adjust");
            x.StatusCodeShouldBe(400);
        });
    }

    [Fact]
    public async Task AdjustInventory_EmptyReason_ReturnsValidationError()
    {
        // Arrange: Initialize inventory
        var sku = "BIRD-CAGE";
        var warehouseId = "main";
        var initialQuantity = 25;

        var initCommand = new InitializeInventory(sku, warehouseId, initialQuantity);
        await _fixture.ExecuteAndWaitAsync(initCommand);

        // Act: Try to adjust with empty reason
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new
            {
                adjustmentQuantity = 5,
                reason = "",  // Invalid
                adjustedBy = "warehouse.clerk@crittersupply.com"
            }).ToUrl($"/api/inventory/{sku}/adjust");
            x.StatusCodeShouldBe(400);
        });
    }

    [Fact]
    public async Task AdjustInventory_NonExistentSku_ReturnsNotFound()
    {
        // Act: Try to adjust non-existent inventory
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new
            {
                adjustmentQuantity = 10,
                reason = "Testing",
                adjustedBy = "test@crittersupply.com"
            }).ToUrl("/api/inventory/DOES-NOT-EXIST/adjust");
            x.StatusCodeShouldBe(404);
        });
    }

    [Fact]
    public async Task AdjustInventory_MultipleAdjustments_AccumulatesCorrectly()
    {
        // Arrange: Initialize inventory
        var sku = "REPTILE-HEAT-LAMP";
        var warehouseId = "main";
        var initialQuantity = 100;

        var initCommand = new InitializeInventory(sku, warehouseId, initialQuantity);
        await _fixture.ExecuteAndWaitAsync(initCommand);

        // Act: Multiple adjustments
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new
            {
                adjustmentQuantity = 50,
                reason = "Restock from supplier A",
                adjustedBy = "clerk1@crittersupply.com"
            }).ToUrl($"/api/inventory/{sku}/adjust");
            x.StatusCodeShouldBeOk();
        });

        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new
            {
                adjustmentQuantity = -20,
                reason = "Damaged items removed",
                adjustedBy = "clerk2@crittersupply.com"
            }).ToUrl($"/api/inventory/{sku}/adjust");
            x.StatusCodeShouldBeOk();
        });

        var finalResult = await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new
            {
                adjustmentQuantity = 10,
                reason = "Cycle count adjustment",
                adjustedBy = "manager@crittersupply.com"
            }).ToUrl($"/api/inventory/{sku}/adjust");
            x.StatusCodeShouldBeOk();
        });

        // Assert: Verify final quantity
        var response = finalResult.ReadAsJson<AdjustInventoryResult>();
        response.NewAvailableQuantity.ShouldBe(140);  // 100 + 50 - 20 + 10
    }
}
