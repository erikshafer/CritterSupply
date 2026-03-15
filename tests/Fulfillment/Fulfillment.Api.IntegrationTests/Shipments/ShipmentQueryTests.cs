using Fulfillment.Shipments;
using Marten;
using Shouldly;

namespace Fulfillment.Api.IntegrationTests.Shipments;

/// <summary>
/// Integration tests for shipment query HTTP endpoints.
/// Tests GET /api/fulfillment/shipments?orderId={id} endpoint.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class ShipmentQueryTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public ShipmentQueryTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.CleanAllDocumentsAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Test GET /api/fulfillment/shipments?orderId={id} returns shipments for an order.
    /// </summary>
    [Fact]
    public async Task GetShipmentsForOrder_ExistingOrder_ReturnsShipments()
    {
        // Arrange: Create a shipment
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var address = new ShippingAddress(
            "123 Test St",
            null,
            "Denver",
            "CO",
            "80202",
            "USA");

        var requestCommand = new RequestFulfillment(
            orderId,
            customerId,
            address,
            new List<FulfillmentLineItem> { new("SKU-TEST-001", 2) },
            "Standard");

        await _fixture.ExecuteAndWaitAsync(requestCommand);

        // Act: Query shipments via HTTP endpoint
        var result = await _fixture.Host.Scenario(x =>
        {
            x.Get.Url($"/api/fulfillment/shipments?orderId={orderId}");
            x.StatusCodeShouldBeOk();
        });

        // Assert: Verify response structure and values
        var response = result.ReadAsJson<List<Api.Queries.ShipmentResponse>>();
        response.ShouldNotBeNull();
        response.Count.ShouldBe(1);

        var shipment = response[0];
        shipment.OrderId.ShouldBe(orderId);
        shipment.Status.ShouldBe(ShipmentStatus.Pending);
        shipment.Carrier.ShouldBeNull();
        shipment.TrackingNumber.ShouldBeNull();
        shipment.WarehouseId.ShouldBeNull();
        shipment.RequestedAt.ShouldNotBe(default);
    }

    /// <summary>
    /// Test GET /api/fulfillment/shipments?orderId={id} returns empty list for order with no shipments.
    /// </summary>
    [Fact]
    public async Task GetShipmentsForOrder_NonexistentOrder_ReturnsEmptyList()
    {
        // Arrange: Use an order ID that doesn't exist
        var nonexistentOrderId = Guid.NewGuid();

        // Act: Query shipments
        var result = await _fixture.Host.Scenario(x =>
        {
            x.Get.Url($"/api/fulfillment/shipments?orderId={nonexistentOrderId}");
            x.StatusCodeShouldBeOk();
        });

        // Assert: Empty list
        var response = result.ReadAsJson<List<Api.Queries.ShipmentResponse>>();
        response.ShouldNotBeNull();
        response.Count.ShouldBe(0);
    }

    /// <summary>
    /// Test GET /api/fulfillment/shipments?orderId={id} returns shipment with tracking info after dispatch.
    /// </summary>
    [Fact]
    public async Task GetShipmentsForOrder_DispatchedShipment_ReturnsTrackingInfo()
    {
        // Arrange: Create and dispatch a shipment
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var address = new ShippingAddress(
            "456 Test Ave",
            null,
            "Seattle",
            "WA",
            "98101",
            "USA");

        var requestCommand = new RequestFulfillment(
            orderId,
            customerId,
            address,
            new List<FulfillmentLineItem> { new("SKU-TEST-002", 1) },
            "Express");

        await _fixture.ExecuteAndWaitAsync(requestCommand);

        // Get shipment ID
        await using var session = _fixture.GetDocumentSession();
        var shipment = await session.Query<Shipment>()
            .FirstAsync(s => s.OrderId == orderId);
        var shipmentId = shipment.Id;

        // Assign warehouse and dispatch
        await _fixture.ExecuteAndWaitAsync(new AssignWarehouse(shipmentId, "WH-01"));
        await _fixture.ExecuteAndWaitAsync(new DispatchShipment(shipmentId, "UPS", "1Z999AA10123456789"));

        // Act: Query shipments
        var result = await _fixture.Host.Scenario(x =>
        {
            x.Get.Url($"/api/fulfillment/shipments?orderId={orderId}");
            x.StatusCodeShouldBeOk();
        });

        // Assert: Verify dispatched shipment details
        var response = result.ReadAsJson<List<Api.Queries.ShipmentResponse>>();
        response.ShouldNotBeNull();
        response.Count.ShouldBe(1);

        var shipmentResponse = response[0];
        shipmentResponse.Status.ShouldBe(ShipmentStatus.Shipped);
        shipmentResponse.Carrier.ShouldBe("UPS");
        shipmentResponse.TrackingNumber.ShouldBe("1Z999AA10123456789");
        shipmentResponse.WarehouseId.ShouldBe("WH-01");
        shipmentResponse.DispatchedAt.ShouldNotBeNull();
    }
}
