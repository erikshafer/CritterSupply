using Fulfillment.Shipments;
using Marten;
using Shouldly;

namespace Fulfillment.Api.IntegrationTests.Shipments;

/// <summary>
/// Integration tests for RequestFulfillment command.
/// Tests creating new shipments and verifying state.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class RequestFulfillmentTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public RequestFulfillmentTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.CleanAllDocumentsAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Integration test for successful fulfillment request.
    /// Creates shipment, verifies initial state is Pending.
    /// </summary>
    [Fact]
    public async Task RequestFulfillment_Creates_Shipment_In_Pending_Status()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var shippingAddress = new ShippingAddress(
            "123 Main St",
            "Apt 4",
            "Seattle",
            "WA",
            "98101",
            "USA");
        var lineItems = new List<FulfillmentLineItem>
        {
            new("SKU-001", 2),
            new("SKU-002", 1)
        };

        var command = new RequestFulfillment(
            orderId,
            customerId,
            shippingAddress,
            lineItems,
            "Standard");

        // Act: Request fulfillment
        await _fixture.ExecuteAndWaitAsync(command);

        // Assert: Verify shipment created with correct initial state
        await using var session = _fixture.GetDocumentSession();
        var shipments = await session.Query<Shipment>()
            .Where(s => s.OrderId == orderId)
            .ToListAsync();

        shipments.ShouldNotBeEmpty();
        var shipment = shipments.First();

        shipment.OrderId.ShouldBe(orderId);
        shipment.CustomerId.ShouldBe(customerId);
        shipment.Status.ShouldBe(ShipmentStatus.Pending);
        shipment.ShippingMethod.ShouldBe("Standard");
        shipment.LineItems.Count.ShouldBe(2);
        shipment.WarehouseId.ShouldBeNull();
        shipment.Carrier.ShouldBeNull();
        shipment.TrackingNumber.ShouldBeNull();

        // Verify shipping address
        shipment.ShippingAddress.AddressLine1.ShouldBe("123 Main St");
        shipment.ShippingAddress.City.ShouldBe("Seattle");
        shipment.ShippingAddress.PostalCode.ShouldBe("98101");

        // Verify line items
        shipment.LineItems[0].Sku.ShouldBe("SKU-001");
        shipment.LineItems[0].Quantity.ShouldBe(2);
        shipment.LineItems[1].Sku.ShouldBe("SKU-002");
        shipment.LineItems[1].Quantity.ShouldBe(1);
    }

    /// <summary>
    /// Integration test for multiple shipments.
    /// Verifies each order gets its own shipment.
    /// </summary>
    [Fact]
    public async Task RequestFulfillment_For_Multiple_Orders_Creates_Separate_Shipments()
    {
        // Arrange: Create two orders
        var order1Id = Guid.NewGuid();
        var order2Id = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        var address = new ShippingAddress(
            "456 Elm St",
            null,
            "Portland",
            "OR",
            "97201",
            "USA");

        var command1 = new RequestFulfillment(
            order1Id,
            customerId,
            address,
            new List<FulfillmentLineItem> { new("SKU-100", 1) },
            "Express");

        var command2 = new RequestFulfillment(
            order2Id,
            customerId,
            address,
            new List<FulfillmentLineItem> { new("SKU-200", 2) },
            "Standard");

        // Act: Request fulfillment for both orders
        await _fixture.ExecuteAndWaitAsync(command1);
        await _fixture.ExecuteAndWaitAsync(command2);

        // Assert: Verify two separate shipments created
        await using var session = _fixture.GetDocumentSession();

        var shipment1 = await session.Query<Shipment>()
            .FirstAsync(s => s.OrderId == order1Id);
        var shipment2 = await session.Query<Shipment>()
            .FirstAsync(s => s.OrderId == order2Id);

        shipment1.Id.ShouldNotBe(shipment2.Id);
        shipment1.OrderId.ShouldBe(order1Id);
        shipment2.OrderId.ShouldBe(order2Id);
        shipment1.ShippingMethod.ShouldBe("Express");
        shipment2.ShippingMethod.ShouldBe("Standard");
    }
}
