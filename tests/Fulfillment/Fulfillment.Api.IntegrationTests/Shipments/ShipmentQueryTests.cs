using Fulfillment.Shipments;
using Marten;
using Messages.Contracts.Common;
using Shouldly;
using IntegrationContracts = Messages.Contracts.Fulfillment;

namespace Fulfillment.Api.IntegrationTests.Shipments;

/// <summary>
/// Integration tests for shipment query HTTP endpoints.
/// Updated for the remastered Shipment aggregate.
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

    [Fact]
    public async Task GetShipmentsForOrder_ExistingOrder_ReturnsShipments()
    {
        var orderId = Guid.NewGuid();
        var message = new IntegrationContracts.FulfillmentRequested(
            orderId,
            Guid.NewGuid(),
            new SharedShippingAddress
            {
                AddressLine1 = "123 Test St",
                City = "Denver",
                StateProvince = "CO",
                PostalCode = "80202",
                Country = "USA"
            },
            new List<IntegrationContracts.FulfillmentLineItem> { new("SKU-TEST-001", 2) },
            "Standard",
            DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(message);

        var result = await _fixture.Host.Scenario(x =>
        {
            x.Get.Url($"/api/fulfillment/shipments?orderId={orderId}");
            x.StatusCodeShouldBeOk();
        });

        var response = result.ReadAsJson<List<Api.OrderFulfillment.ShipmentResponse>>();
        response.ShouldNotBeNull();
        response.Count.ShouldBe(1);

        var shipment = response[0];
        shipment.OrderId.ShouldBe(orderId);
        shipment.Status.ShouldBe(ShipmentStatus.Assigned);
        shipment.AssignedFulfillmentCenter.ShouldNotBeNullOrEmpty();
        shipment.RequestedAt.ShouldNotBe(default);
    }

    [Fact]
    public async Task GetShipmentsForOrder_NonexistentOrder_ReturnsEmptyList()
    {
        var nonexistentOrderId = Guid.NewGuid();

        var result = await _fixture.Host.Scenario(x =>
        {
            x.Get.Url($"/api/fulfillment/shipments?orderId={nonexistentOrderId}");
            x.StatusCodeShouldBeOk();
        });

        var response = result.ReadAsJson<List<Api.OrderFulfillment.ShipmentResponse>>();
        response.ShouldNotBeNull();
        response.Count.ShouldBe(0);
    }
}
