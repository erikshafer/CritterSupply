using Inventory.Management;
using Marten;
using Shouldly;
using static Inventory.Api.Commands.ReceiveInboundStockEndpoint;

namespace Inventory.Api.IntegrationTests.Commands;

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
    public async Task ReceiveInboundStock_ValidQuantity_IncreasesInventory()
    {
        // Arrange: Product with initial inventory
        await using var session = _fixture.GetDocumentSession();
        var sku = "HAMSTER-WHEEL-001";
        var warehouseId = "main";
        var inventoryId = ProductInventory.CombinedGuid(sku, warehouseId);

        session.Events.StartStream<ProductInventory>(
            inventoryId,
            new InventoryInitialized(sku, warehouseId, 0, DateTimeOffset.UtcNow.AddDays(-1)),
            new StockReceived(20, "Initial stock", DateTimeOffset.UtcNow.AddDays(-1)));

        await session.SaveChangesAsync();

        // Act: Receive inbound stock
        var result = await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new ReceiveInboundStockRequest(
                Quantity: 50,
                Source: "Supplier: Acme Pet Supplies PO#12345")).ToUrl($"/api/inventory/{sku}/receive");
            x.StatusCodeShouldBeOk();
        });

        // Assert: Verify quantity increased
        var response = result.ReadAsJson<ReceiveInboundStockResult>();
        response.ShouldNotBeNull();
        response.Sku.ShouldBe(sku);
        response.NewAvailableQuantity.ShouldBe(70); // 20 + 50
    }

    [Fact]
    public async Task ReceiveInboundStock_MultipleShipments_AccumulatesQuantity()
    {
        // Arrange: Product with initial inventory
        await using var session = _fixture.GetDocumentSession();
        var sku = "RABBIT-HUTCH-002";
        var warehouseId = "main";
        var inventoryId = ProductInventory.CombinedGuid(sku, warehouseId);

        session.Events.StartStream<ProductInventory>(
            inventoryId,
            new InventoryInitialized(sku, warehouseId, 0, DateTimeOffset.UtcNow.AddDays(-2)),
            new StockReceived(10, "Initial stock", DateTimeOffset.UtcNow.AddDays(-2)));

        await session.SaveChangesAsync();

        // Act: Receive multiple shipments
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new ReceiveInboundStockRequest(
                Quantity: 25,
                Source: "Supplier: Vendor A PO#111")).ToUrl($"/api/inventory/{sku}/receive");
            x.StatusCodeShouldBeOk();
        });

        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new ReceiveInboundStockRequest(
                Quantity: 15,
                Source: "Supplier: Vendor B PO#222")).ToUrl($"/api/inventory/{sku}/receive");
            x.StatusCodeShouldBeOk();
        });

        var result = await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new ReceiveInboundStockRequest(
                Quantity: 30,
                Source: "Transfer from Warehouse South")).ToUrl($"/api/inventory/{sku}/receive");
            x.StatusCodeShouldBeOk();
        });

        // Assert: Verify net quantity (10 + 25 + 15 + 30 = 80)
        var response = result.ReadAsJson<ReceiveInboundStockResult>();
        response.ShouldNotBeNull();
        response.NewAvailableQuantity.ShouldBe(80);
    }

    [Fact]
    public async Task ReceiveInboundStock_NonExistentSku_ReturnsNotFound()
    {
        // Act: Try to receive stock for non-existent SKU
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new ReceiveInboundStockRequest(
                Quantity: 100,
                Source: "Supplier: Test PO#999")).ToUrl("/api/inventory/DOES-NOT-EXIST/receive");
            x.StatusCodeShouldBe(404);
        });
    }

    [Fact]
    public async Task ReceiveInboundStock_ZeroQuantity_ReturnsBadRequest()
    {
        // Arrange: Product with inventory
        await using var session = _fixture.GetDocumentSession();
        var sku = "GUINEA-PIG-FOOD-003";
        var warehouseId = "main";
        var inventoryId = ProductInventory.CombinedGuid(sku, warehouseId);

        session.Events.StartStream<ProductInventory>(
            inventoryId,
            new InventoryInitialized(sku, warehouseId, 0, DateTimeOffset.UtcNow.AddDays(-1)));

        await session.SaveChangesAsync();

        // Act: Try to receive zero quantity
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new ReceiveInboundStockRequest(
                Quantity: 0,
                Source: "Test source")).ToUrl($"/api/inventory/{sku}/receive");
            x.StatusCodeShouldBe(400);
        });
    }

    [Fact]
    public async Task ReceiveInboundStock_NegativeQuantity_ReturnsBadRequest()
    {
        // Arrange: Product with inventory
        await using var session = _fixture.GetDocumentSession();
        var sku = "LIZARD-LAMP-004";
        var warehouseId = "main";
        var inventoryId = ProductInventory.CombinedGuid(sku, warehouseId);

        session.Events.StartStream<ProductInventory>(
            inventoryId,
            new InventoryInitialized(sku, warehouseId, 0, DateTimeOffset.UtcNow.AddDays(-1)));

        await session.SaveChangesAsync();

        // Act: Try to receive negative quantity
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new ReceiveInboundStockRequest(
                Quantity: -10,
                Source: "Test source")).ToUrl($"/api/inventory/{sku}/receive");
            x.StatusCodeShouldBe(400);
        });
    }

    [Fact]
    public async Task ReceiveInboundStock_EmptySource_ReturnsBadRequest()
    {
        // Arrange: Product with inventory
        await using var session = _fixture.GetDocumentSession();
        var sku = "TURTLE-TANK-005";
        var warehouseId = "main";
        var inventoryId = ProductInventory.CombinedGuid(sku, warehouseId);

        session.Events.StartStream<ProductInventory>(
            inventoryId,
            new InventoryInitialized(sku, warehouseId, 0, DateTimeOffset.UtcNow.AddDays(-1)));

        await session.SaveChangesAsync();

        // Act: Try to receive with empty source
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new ReceiveInboundStockRequest(
                Quantity: 25,
                Source: "")).ToUrl($"/api/inventory/{sku}/receive");
            x.StatusCodeShouldBe(400);
        });
    }

    [Fact]
    public async Task ReceiveInboundStock_LargeQuantity_HandlesCorrectly()
    {
        // Arrange: Product with low inventory
        await using var session = _fixture.GetDocumentSession();
        var sku = "PARROT-PERCH-006";
        var warehouseId = "main";
        var inventoryId = ProductInventory.CombinedGuid(sku, warehouseId);

        session.Events.StartStream<ProductInventory>(
            inventoryId,
            new InventoryInitialized(sku, warehouseId, 0, DateTimeOffset.UtcNow.AddDays(-1)),
            new StockReceived(5, "Initial stock", DateTimeOffset.UtcNow.AddDays(-1)));

        await session.SaveChangesAsync();

        // Act: Receive large bulk shipment
        var result = await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new ReceiveInboundStockRequest(
                Quantity: 5000,
                Source: "Supplier: Mega Pet Warehouse - Bulk Order PO#99999")).ToUrl($"/api/inventory/{sku}/receive");
            x.StatusCodeShouldBeOk();
        });

        // Assert: Verify large quantity handled correctly
        var response = result.ReadAsJson<ReceiveInboundStockResult>();
        response.ShouldNotBeNull();
        response.NewAvailableQuantity.ShouldBe(5005); // 5 + 5000
    }
}
