using Inventory.Api.StockQueries;
using Inventory.Management;

namespace Inventory.Api.IntegrationTests.StockQueries;

/// <summary>
/// Integration tests for GET /api/inventory/availability/{sku} endpoint.
/// Verifies per-warehouse stock availability responses powered by the inline
/// <see cref="StockAvailabilityViewProjection"/>.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class StockAvailabilityEndpointTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public StockAvailabilityEndpointTests(TestFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.CleanAllDocumentsAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Unknown_Sku_Returns_Empty_Warehouses_With_Zero_TotalAvailable()
    {
        var result = await _fixture.Host.Scenario(x =>
        {
            x.Get.Url("/api/inventory/availability/NONEXISTENT-SKU");
            x.StatusCodeShouldBeOk();
        });

        var response = result.ReadAsJson<StockAvailabilityResponse>();
        response.ShouldNotBeNull();
        response.Sku.ShouldBe("NONEXISTENT-SKU");
        response.Warehouses.Count.ShouldBe(0);
        response.TotalAvailable.ShouldBe(0);
    }

    [Fact]
    public async Task Single_Warehouse_Returns_Correct_Quantity()
    {
        // Arrange: seed inventory via event stream
        var sku = "AVAIL-SINGLE-001";
        var warehouseId = "NJ-FC";
        var streamId = InventoryStreamId.Compute(sku, warehouseId);

        await using var session = _fixture.GetDocumentSession();
        session.Events.StartStream<ProductInventory>(
            streamId,
            new InventoryInitialized(sku, warehouseId, 80, DateTimeOffset.UtcNow));
        await session.SaveChangesAsync();

        // Act
        var result = await _fixture.Host.Scenario(x =>
        {
            x.Get.Url($"/api/inventory/availability/{sku}");
            x.StatusCodeShouldBeOk();
        });

        // Assert
        var response = result.ReadAsJson<StockAvailabilityResponse>();
        response.ShouldNotBeNull();
        response.Sku.ShouldBe(sku);
        response.Warehouses.Count.ShouldBe(1);
        response.Warehouses[0].WarehouseId.ShouldBe(warehouseId);
        response.Warehouses[0].AvailableQuantity.ShouldBe(80);
        response.TotalAvailable.ShouldBe(80);
    }

    [Fact]
    public async Task Multi_Warehouse_Returns_All_Warehouses()
    {
        // Arrange: initialize two warehouses for the same SKU
        var sku = "AVAIL-MULTI-001";
        var njStreamId = InventoryStreamId.Compute(sku, "NJ-FC");
        var ohStreamId = InventoryStreamId.Compute(sku, "OH-FC");

        await using var session = _fixture.GetDocumentSession();
        session.Events.StartStream<ProductInventory>(
            njStreamId,
            new InventoryInitialized(sku, "NJ-FC", 100, DateTimeOffset.UtcNow));
        session.Events.StartStream<ProductInventory>(
            ohStreamId,
            new InventoryInitialized(sku, "OH-FC", 60, DateTimeOffset.UtcNow));
        await session.SaveChangesAsync();

        // Act
        var result = await _fixture.Host.Scenario(x =>
        {
            x.Get.Url($"/api/inventory/availability/{sku}");
            x.StatusCodeShouldBeOk();
        });

        // Assert
        var response = result.ReadAsJson<StockAvailabilityResponse>();
        response.ShouldNotBeNull();
        response.Sku.ShouldBe(sku);
        response.Warehouses.Count.ShouldBe(2);
        response.Warehouses.ShouldContain(w => w.WarehouseId == "NJ-FC" && w.AvailableQuantity == 100);
        response.Warehouses.ShouldContain(w => w.WarehouseId == "OH-FC" && w.AvailableQuantity == 60);
        response.TotalAvailable.ShouldBe(160);
    }

    [Fact]
    public async Task After_Reservation_Availability_Reflects_Reduced_Quantity()
    {
        // Arrange: initialize inventory then reserve stock
        var sku = "AVAIL-RESERVE-001";
        var warehouseId = "NJ-FC";

        await _fixture.ExecuteAndWaitAsync(new InitializeInventory(sku, warehouseId, 100));
        await _fixture.ExecuteAndWaitAsync(
            new ReserveStock(Guid.NewGuid(), sku, warehouseId, Guid.NewGuid(), 35));

        // Act: query availability — inline projection should already reflect the reservation
        var result = await _fixture.Host.Scenario(x =>
        {
            x.Get.Url($"/api/inventory/availability/{sku}");
            x.StatusCodeShouldBeOk();
        });

        // Assert
        var response = result.ReadAsJson<StockAvailabilityResponse>();
        response.ShouldNotBeNull();
        response.TotalAvailable.ShouldBe(65); // 100 - 35
        response.Warehouses.Count.ShouldBe(1);
        response.Warehouses[0].AvailableQuantity.ShouldBe(65);
    }
}
