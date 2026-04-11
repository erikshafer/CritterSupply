using Inventory.Api.StockQueries;
using Inventory.Management;

namespace Inventory.Api.IntegrationTests.Management;

/// <summary>
/// Integration tests for FC capacity HTTP endpoint and projection.
/// Slice 39 (P3): GET /api/inventory/fc-capacity/{warehouseId}.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class FcCapacityTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public FcCapacityTests(TestFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.CleanAllDocumentsAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task FcCapacity_Returns_Zero_For_Unknown_Warehouse()
    {
        var result = await _fixture.Host.Scenario(x =>
        {
            x.Get.Url("/api/inventory/fc-capacity/UNKNOWN-WH");
            x.StatusCodeShouldBeOk();
        });

        var response = result.ReadAsJson<FcCapacityResponse>();
        response.ShouldNotBeNull();
        response.WarehouseId.ShouldBe("UNKNOWN-WH");
        response.TotalAvailable.ShouldBe(0);
        response.SkuCount.ShouldBe(0);
    }

    [Fact]
    public async Task FcCapacity_Aggregates_Multiple_SKUs_At_Same_Warehouse()
    {
        var wh = "WH-FC-CAP";
        await _fixture.ExecuteAndWaitAsync(new InitializeInventory("SKU-A", wh, 100));
        await _fixture.ExecuteAndWaitAsync(new InitializeInventory("SKU-B", wh, 50));
        await _fixture.ExecuteAndWaitAsync(new InitializeInventory("SKU-C", wh, 200));

        var result = await _fixture.Host.Scenario(x =>
        {
            x.Get.Url($"/api/inventory/fc-capacity/{wh}");
            x.StatusCodeShouldBeOk();
        });

        var response = result.ReadAsJson<FcCapacityResponse>();
        response.ShouldNotBeNull();
        response.WarehouseId.ShouldBe(wh);
        response.SkuCount.ShouldBe(3);
        response.TotalAvailable.ShouldBe(350);
        response.TotalOnHand.ShouldBe(350);
    }

    [Fact]
    public async Task FcCapacity_Reflects_Reservation_Activity()
    {
        var wh = "WH-FC-RES";
        await _fixture.ExecuteAndWaitAsync(new InitializeInventory("SKU-R1", wh, 100));

        var orderId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        await _fixture.ExecuteAndWaitAsync(
            new ReserveStock(orderId, "SKU-R1", wh, reservationId, 30));

        var result = await _fixture.Host.Scenario(x =>
        {
            x.Get.Url($"/api/inventory/fc-capacity/{wh}");
            x.StatusCodeShouldBeOk();
        });

        var response = result.ReadAsJson<FcCapacityResponse>();
        response.ShouldNotBeNull();
        response.TotalAvailable.ShouldBe(70);
        response.TotalReserved.ShouldBe(30);
        response.TotalOnHand.ShouldBe(100);
    }
}
