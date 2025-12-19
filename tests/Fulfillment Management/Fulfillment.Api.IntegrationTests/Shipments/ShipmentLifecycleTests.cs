using Fulfillment.Shipments;
using Marten;
using Shouldly;

namespace Fulfillment.Api.IntegrationTests.Shipments;

/// <summary>
/// Integration tests for full shipment lifecycle.
/// Tests the complete flow: Pending → Assigned → Shipped → Delivered.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class ShipmentLifecycleTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public ShipmentLifecycleTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.CleanAllDocumentsAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Integration test for full happy path: Request → Assign → Dispatch → Deliver.
    /// </summary>
    [Fact]
    public async Task Complete_Shipment_Lifecycle_Success()
    {
        // Arrange: Create shipment
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var address = new ShippingAddress(
            "789 Oak Ave",
            null,
            "Denver",
            "CO",
            "80202",
            "USA");

        var requestCommand = new RequestFulfillment(
            orderId,
            customerId,
            address,
            new List<FulfillmentLineItem> { new("SKU-300", 3) },
            "Express");

        await _fixture.ExecuteAndWaitAsync(requestCommand);

        // Get the shipment ID
        await using var session1 = _fixture.GetDocumentSession();
        var shipment = await session1.Query<Shipment>()
            .FirstAsync(s => s.OrderId == orderId);
        var shipmentId = shipment.Id;

        // Act 1: Assign warehouse
        var assignCommand = new AssignWarehouse(shipmentId, "WH-WEST");
        await _fixture.ExecuteAndWaitAsync(assignCommand);

        // Assert 1: Verify assigned
        await using var session2 = _fixture.GetDocumentSession();
        var assignedShipment = await session2.LoadAsync<Shipment>(shipmentId);
        assignedShipment.Status.ShouldBe(ShipmentStatus.Assigned);
        assignedShipment.WarehouseId.ShouldBe("WH-WEST");
        assignedShipment.AssignedAt.ShouldNotBeNull();

        // Act 2: Dispatch shipment
        var dispatchCommand = new DispatchShipment(shipmentId, "FedEx", "1Z999AA10123456784");
        await _fixture.ExecuteAndWaitAsync(dispatchCommand);

        // Assert 2: Verify dispatched
        await using var session3 = _fixture.GetDocumentSession();
        var dispatchedShipment = await session3.LoadAsync<Shipment>(shipmentId);
        dispatchedShipment.Status.ShouldBe(ShipmentStatus.Shipped);
        dispatchedShipment.Carrier.ShouldBe("FedEx");
        dispatchedShipment.TrackingNumber.ShouldBe("1Z999AA10123456784");
        dispatchedShipment.DispatchedAt.ShouldNotBeNull();

        // Act 3: Confirm delivery
        var confirmCommand = new ConfirmDelivery(shipmentId, "John Doe");
        await _fixture.ExecuteAndWaitAsync(confirmCommand);

        // Assert 3: Verify delivered
        await using var session4 = _fixture.GetDocumentSession();
        var deliveredShipment = await session4.LoadAsync<Shipment>(shipmentId);
        deliveredShipment.Status.ShouldBe(ShipmentStatus.Delivered);
        deliveredShipment.DeliveredAt.ShouldNotBeNull();
    }

    /// <summary>
    /// Integration test for validating status transitions.
    /// Cannot dispatch before assigning warehouse.
    /// </summary>
    [Fact]
    public async Task Dispatch_Without_Assignment_Fails()
    {
        // Arrange: Create shipment but don't assign warehouse
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var address = new ShippingAddress(
            "321 Pine St",
            null,
            "Austin",
            "TX",
            "78701",
            "USA");

        var requestCommand = new RequestFulfillment(
            orderId,
            customerId,
            address,
            new List<FulfillmentLineItem> { new("SKU-400", 1) },
            "Standard");

        await _fixture.ExecuteAndWaitAsync(requestCommand);

        // Get the shipment ID
        await using var session1 = _fixture.GetDocumentSession();
        var shipment = await session1.Query<Shipment>()
            .FirstAsync(s => s.OrderId == orderId);
        var shipmentId = shipment.Id;

        // Act: Try to dispatch without assigning (should fail validation)
        var dispatchCommand = new DispatchShipment(shipmentId, "UPS", "1Z999AA10123456785");
        await _fixture.ExecuteAndWaitAsync(dispatchCommand);

        // Assert: Verify shipment is still in Pending status
        await using var session2 = _fixture.GetDocumentSession();
        var unchangedShipment = await session2.LoadAsync<Shipment>(shipmentId);
        unchangedShipment.Status.ShouldBe(ShipmentStatus.Pending);
        unchangedShipment.Carrier.ShouldBeNull();
        unchangedShipment.TrackingNumber.ShouldBeNull();
    }

    /// <summary>
    /// Integration test for delivery confirmation validation.
    /// Cannot confirm delivery before dispatch.
    /// </summary>
    [Fact]
    public async Task ConfirmDelivery_Without_Dispatch_Fails()
    {
        // Arrange: Create and assign shipment, but don't dispatch
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var address = new ShippingAddress(
            "555 Maple Dr",
            null,
            "Boston",
            "MA",
            "02101",
            "USA");

        var requestCommand = new RequestFulfillment(
            orderId,
            customerId,
            address,
            new List<FulfillmentLineItem> { new("SKU-500", 2) },
            "Express");

        await _fixture.ExecuteAndWaitAsync(requestCommand);

        // Get shipment and assign warehouse
        await using var session1 = _fixture.GetDocumentSession();
        var shipment = await session1.Query<Shipment>()
            .FirstAsync(s => s.OrderId == orderId);
        var shipmentId = shipment.Id;

        var assignCommand = new AssignWarehouse(shipmentId, "WH-EAST");
        await _fixture.ExecuteAndWaitAsync(assignCommand);

        // Act: Try to confirm delivery without dispatching (should fail validation)
        var confirmCommand = new ConfirmDelivery(shipmentId, "Jane Smith");
        await _fixture.ExecuteAndWaitAsync(confirmCommand);

        // Assert: Verify shipment is still Assigned, not Delivered
        await using var session2 = _fixture.GetDocumentSession();
        var unchangedShipment = await session2.LoadAsync<Shipment>(shipmentId);
        unchangedShipment.Status.ShouldBe(ShipmentStatus.Assigned);
        unchangedShipment.DeliveredAt.ShouldBeNull();
    }

    /// <summary>
    /// Integration test for multiple shipments with different warehouses.
    /// Verifies warehouse assignment is per-shipment.
    /// </summary>
    [Fact]
    public async Task Multiple_Shipments_Can_Have_Different_Warehouses()
    {
        // Arrange: Create two shipments
        var order1Id = Guid.NewGuid();
        var order2Id = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var address = new ShippingAddress(
            "999 Test St",
            null,
            "Miami",
            "FL",
            "33101",
            "USA");

        var command1 = new RequestFulfillment(
            order1Id,
            customerId,
            address,
            new List<FulfillmentLineItem> { new("SKU-600", 1) },
            "Standard");

        var command2 = new RequestFulfillment(
            order2Id,
            customerId,
            address,
            new List<FulfillmentLineItem> { new("SKU-700", 1) },
            "Standard");

        await _fixture.ExecuteAndWaitAsync(command1);
        await _fixture.ExecuteAndWaitAsync(command2);

        // Get shipment IDs
        await using var session1 = _fixture.GetDocumentSession();
        var shipment1 = await session1.Query<Shipment>()
            .FirstAsync(s => s.OrderId == order1Id);
        var shipment2 = await session1.Query<Shipment>()
            .FirstAsync(s => s.OrderId == order2Id);

        // Act: Assign different warehouses
        await _fixture.ExecuteAndWaitAsync(new AssignWarehouse(shipment1.Id, "WH-NORTH"));
        await _fixture.ExecuteAndWaitAsync(new AssignWarehouse(shipment2.Id, "WH-SOUTH"));

        // Assert: Verify each has correct warehouse
        await using var session2 = _fixture.GetDocumentSession();
        var assigned1 = await session2.LoadAsync<Shipment>(shipment1.Id);
        var assigned2 = await session2.LoadAsync<Shipment>(shipment2.Id);

        assigned1.WarehouseId.ShouldBe("WH-NORTH");
        assigned2.WarehouseId.ShouldBe("WH-SOUTH");
        assigned1.Status.ShouldBe(ShipmentStatus.Assigned);
        assigned2.Status.ShouldBe(ShipmentStatus.Assigned);
    }
}
