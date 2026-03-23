using Inventory.Api.InventoryManagement;
using Inventory.Management;
using Marten;
using Shouldly;

namespace Inventory.Api.IntegrationTests.Management;

/// <summary>
/// Integration tests for POST /api/inventory/{sku}/receive endpoint.
/// Tests the ReceiveInboundStock write operation added in M32.1 Session 3.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class ReceiveInboundStockEndpointTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public ReceiveInboundStockEndpointTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.CleanAllDocumentsAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ReceiveInboundStock_ValidShipment_IncreasesStock()
    {
        // Arrange: Initialize inventory
        var sku = "DOG-LEASH-6FT";
        var warehouseId = "main";
        var initialQuantity = 30;

        var initCommand = new InitializeInventory(sku, warehouseId, initialQuantity);
        await _fixture.ExecuteAndWaitAsync(initCommand);

        // Act: Receive inbound shipment
        var result = await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new
            {
                quantity = 50,
                source = "Supplier: Acme Pet Supplies PO#12345"
            }).ToUrl($"/api/inventory/{sku}/receive");
            x.StatusCodeShouldBeOk();
        });

        // Assert: Verify new quantity
        var response = result.ReadAsJson<ReceiveInboundStockResult>();
        response.ShouldNotBeNull();
        response.Sku.ShouldBe(sku);
        response.WarehouseId.ShouldBe(warehouseId);
        response.NewAvailableQuantity.ShouldBe(80);  // 30 + 50
    }

    [Fact]
    public async Task ReceiveInboundStock_MultipleShipments_AccumulatesStock()
    {
        // Arrange: Initialize inventory
        var sku = "CAT-FOOD-5LB";
        var warehouseId = "main";
        var initialQuantity = 100;

        var initCommand = new InitializeInventory(sku, warehouseId, initialQuantity);
        await _fixture.ExecuteAndWaitAsync(initCommand);

        // Act: Receive multiple shipments
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new
            {
                quantity = 200,
                source = "Supplier: Premium Pet Foods PO#AAA-001"
            }).ToUrl($"/api/inventory/{sku}/receive");
            x.StatusCodeShouldBeOk();
        });

        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new
            {
                quantity = 100,
                source = "Supplier: Premium Pet Foods PO#AAA-002"
            }).ToUrl($"/api/inventory/{sku}/receive");
            x.StatusCodeShouldBeOk();
        });

        var finalResult = await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new
            {
                quantity = 150,
                source = "Supplier: Budget Pet Foods PO#BBB-500"
            }).ToUrl($"/api/inventory/{sku}/receive");
            x.StatusCodeShouldBeOk();
        });

        // Assert: Verify accumulated quantity
        var response = finalResult.ReadAsJson<ReceiveInboundStockResult>();
        response.NewAvailableQuantity.ShouldBe(550);  // 100 + 200 + 100 + 150
    }

    [Fact]
    public async Task ReceiveInboundStock_ZeroQuantity_ReturnsValidationError()
    {
        // Arrange: Initialize inventory
        var sku = "BIRD-SEED-5LB";
        var warehouseId = "main";
        var initialQuantity = 50;

        var initCommand = new InitializeInventory(sku, warehouseId, initialQuantity);
        await _fixture.ExecuteAndWaitAsync(initCommand);

        // Act: Try to receive zero quantity
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new
            {
                quantity = 0,  // Invalid
                source = "Supplier: Test PO#000"
            }).ToUrl($"/api/inventory/{sku}/receive");
            x.StatusCodeShouldBe(400);
        });
    }

    [Fact]
    public async Task ReceiveInboundStock_NegativeQuantity_ReturnsValidationError()
    {
        // Arrange: Initialize inventory
        var sku = "AQUARIUM-GRAVEL";
        var warehouseId = "main";
        var initialQuantity = 75;

        var initCommand = new InitializeInventory(sku, warehouseId, initialQuantity);
        await _fixture.ExecuteAndWaitAsync(initCommand);

        // Act: Try to receive negative quantity
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new
            {
                quantity = -10,  // Invalid
                source = "Supplier: Bad Data"
            }).ToUrl($"/api/inventory/{sku}/receive");
            x.StatusCodeShouldBe(400);
        });
    }

    [Fact]
    public async Task ReceiveInboundStock_EmptySource_ReturnsValidationError()
    {
        // Arrange: Initialize inventory
        var sku = "HAMSTER-WHEEL";
        var warehouseId = "main";
        var initialQuantity = 20;

        var initCommand = new InitializeInventory(sku, warehouseId, initialQuantity);
        await _fixture.ExecuteAndWaitAsync(initCommand);

        // Act: Try to receive with empty source
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new
            {
                quantity = 10,
                source = ""  // Invalid
            }).ToUrl($"/api/inventory/{sku}/receive");
            x.StatusCodeShouldBe(400);
        });
    }

    [Fact]
    public async Task ReceiveInboundStock_NonExistentSku_ReturnsNotFound()
    {
        // Act: Try to receive stock for non-existent SKU
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new
            {
                quantity = 100,
                source = "Supplier: Test PO#999"
            }).ToUrl("/api/inventory/DOES-NOT-EXIST/receive");
            x.StatusCodeShouldBe(404);
        });
    }

    [Fact]
    public async Task ReceiveInboundStock_LargeShipment_HandlesCorrectly()
    {
        // Arrange: Initialize inventory
        var sku = "DOG-FOOD-50LB";
        var warehouseId = "main";
        var initialQuantity = 500;

        var initCommand = new InitializeInventory(sku, warehouseId, initialQuantity);
        await _fixture.ExecuteAndWaitAsync(initCommand);

        // Act: Receive large shipment (pallet quantity)
        var result = await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new
            {
                quantity = 10000,  // Large shipment
                source = "Supplier: Bulk Pet Foods PO#PALLET-2024-001"
            }).ToUrl($"/api/inventory/{sku}/receive");
            x.StatusCodeShouldBeOk();
        });

        // Assert: Verify quantity
        var response = result.ReadAsJson<ReceiveInboundStockResult>();
        response.NewAvailableQuantity.ShouldBe(10500);  // 500 + 10000
    }
}
