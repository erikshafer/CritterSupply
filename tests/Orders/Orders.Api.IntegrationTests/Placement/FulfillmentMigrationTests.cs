using Marten;
using Messages.Contracts.Fulfillment;
using Orders.Placement;
using OrdersShippingAddress = Orders.Placement.ShippingAddress;

namespace Orders.Api.IntegrationTests.Placement;

/// <summary>
/// Integration tests for the Fulfillment Remaster S4 migration.
/// Tests the new Fulfillment contract surface (ShipmentHandedToCarrier, TrackingNumberAssigned,
/// ReturnToSenderInitiated, ReshipmentCreated, BackorderCreated, FulfillmentCancelled,
/// OrderSplitIntoShipments) against the Orders saga.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class FulfillmentMigrationTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public FulfillmentMigrationTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.CleanAllDocumentsAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Helper: Creates an order and advances it to Fulfilling status.
    /// Returns the order so tests can send fulfillment events against it.
    /// </summary>
    private async Task<Order> CreateOrderInFulfillingStatus(string sku = "SKU-MIG-001", decimal unitPrice = 29.99m)
    {
        var customerId = Guid.NewGuid();
        var checkoutCompleted = TestFixture.CreateCheckoutCompletedMessage(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            customerId,
            [new CheckoutLineItem(sku, 1, unitPrice)],
            new OrdersShippingAddress("100 Migration St", null, "Seattle", "WA", "98101", "USA"),
            "Standard",
            5.99m,
            "tok_visa",
            DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(checkoutCompleted);

        Order order;
        await using (var session = _fixture.GetDocumentSession())
        {
            order = (await session.Query<Order>()
                .Where(o => o.CustomerId == customerId)
                .ToListAsync()).First();
        }

        // Payment capture
        await _fixture.ExecuteAndWaitAsync(new Messages.Contracts.Payments.PaymentCaptured(
            Guid.NewGuid(), order.Id, unitPrice + 5.99m, "txn_mig", DateTimeOffset.UtcNow));

        // Inventory reservation + commitment
        var reservationId = Guid.NewGuid();
        await _fixture.ExecuteAndWaitAsync(new Messages.Contracts.Inventory.ReservationConfirmed(
            order.Id, Guid.NewGuid(), reservationId, sku, "WH-01", 1, DateTimeOffset.UtcNow));

        await _fixture.ExecuteAndWaitAsync(new Messages.Contracts.Inventory.ReservationCommitted(
            order.Id, Guid.NewGuid(), reservationId, sku, "WH-01", 1, DateTimeOffset.UtcNow));

        // Verify order is in Fulfilling status
        await using (var session = _fixture.GetDocumentSession())
        {
            var fulfillingOrder = await session.LoadAsync<Order>(order.Id);
            fulfillingOrder!.Status.ShouldBe(OrderStatus.Fulfilling);
        }

        return order;
    }

    /// <summary>
    /// Helper: Advances an order from Fulfilling to Shipped via ShipmentHandedToCarrier.
    /// </summary>
    private async Task<Order> AdvanceToShipped(Order order)
    {
        await _fixture.ExecuteAndWaitAsync(new ShipmentHandedToCarrier(
            order.Id, Guid.NewGuid(), "FedEx", "1Z999AA10123456784", DateTimeOffset.UtcNow));

        await using var session = _fixture.GetDocumentSession();
        var shipped = await session.LoadAsync<Order>(order.Id);
        shipped!.Status.ShouldBe(OrderStatus.Shipped);
        return shipped;
    }

    /// <summary>
    /// Test 1: ShipmentHandedToCarrier transitions order from Fulfilling to Shipped.
    /// This is the replacement for the legacy ShipmentDispatched handler.
    /// </summary>
    [Fact]
    public async Task ShipmentHandedToCarrier_Transitions_To_Shipped()
    {
        // Arrange
        var order = await CreateOrderInFulfillingStatus();

        // Act
        await _fixture.ExecuteAndWaitAsync(new ShipmentHandedToCarrier(
            order.Id, Guid.NewGuid(), "UPS", "1Z999AA10123456784", DateTimeOffset.UtcNow));

        // Assert
        await using var session = _fixture.GetDocumentSession();
        var shippedOrder = await session.LoadAsync<Order>(order.Id);
        shippedOrder!.Status.ShouldBe(OrderStatus.Shipped);
    }

    /// <summary>
    /// Test 2: TrackingNumberAssigned stores tracking number without changing status.
    /// </summary>
    [Fact]
    public async Task TrackingNumberAssigned_Stores_Tracking_Number()
    {
        // Arrange: Get order to Fulfilling (tracking can arrive before carrier handoff)
        var order = await CreateOrderInFulfillingStatus();

        // Act
        await _fixture.ExecuteAndWaitAsync(new TrackingNumberAssigned(
            order.Id, Guid.NewGuid(), "1Z999AA10123456784", "UPS", DateTimeOffset.UtcNow));

        // Assert
        await using var session = _fixture.GetDocumentSession();
        var updatedOrder = await session.LoadAsync<Order>(order.Id);
        updatedOrder!.TrackingNumber.ShouldBe("1Z999AA10123456784");
        updatedOrder.Status.ShouldBe(OrderStatus.Fulfilling); // Status unchanged
    }

    /// <summary>
    /// Test 3: ReturnToSenderInitiated transitions order from Shipped to DeliveryFailed.
    /// </summary>
    [Fact]
    public async Task ReturnToSenderInitiated_Transitions_To_DeliveryFailed()
    {
        // Arrange: Get order to Shipped
        var order = await CreateOrderInFulfillingStatus();
        await AdvanceToShipped(order);

        // Act
        await _fixture.ExecuteAndWaitAsync(new ReturnToSenderInitiated(
            order.Id, Guid.NewGuid(), "FedEx", 3, 7, DateTimeOffset.UtcNow));

        // Assert
        await using var session = _fixture.GetDocumentSession();
        var failedOrder = await session.LoadAsync<Order>(order.Id);
        failedOrder!.Status.ShouldBe(OrderStatus.DeliveryFailed);
    }

    /// <summary>
    /// Test 4: ReshipmentCreated transitions order from DeliveryFailed to Reshipping.
    /// </summary>
    [Fact]
    public async Task ReshipmentCreated_Transitions_To_Reshipping()
    {
        // Arrange: Get order to DeliveryFailed
        var order = await CreateOrderInFulfillingStatus();
        await AdvanceToShipped(order);

        await _fixture.ExecuteAndWaitAsync(new ReturnToSenderInitiated(
            order.Id, Guid.NewGuid(), "FedEx", 3, 7, DateTimeOffset.UtcNow));

        // Act
        var newShipmentId = Guid.NewGuid();
        await _fixture.ExecuteAndWaitAsync(new ReshipmentCreated(
            order.Id, Guid.NewGuid(), newShipmentId, "Customer requested reshipment", DateTimeOffset.UtcNow));

        // Assert
        await using var session = _fixture.GetDocumentSession();
        var reshippingOrder = await session.LoadAsync<Order>(order.Id);
        reshippingOrder!.Status.ShouldBe(OrderStatus.Reshipping);
        reshippingOrder.ActiveReshipmentShipmentId.ShouldBe(newShipmentId);
    }

    /// <summary>
    /// Test 5: BackorderCreated transitions order from Fulfilling to Backordered.
    /// </summary>
    [Fact]
    public async Task BackorderCreated_Transitions_To_Backordered()
    {
        // Arrange
        var order = await CreateOrderInFulfillingStatus();

        // Act
        await _fixture.ExecuteAndWaitAsync(new BackorderCreated(
            order.Id, Guid.NewGuid(), "No stock at any FC",
            [new BackorderedItem("DOG-FOOD-40LB", "NJ-FC", 1)],
            DateTimeOffset.UtcNow));

        // Assert
        await using var session = _fixture.GetDocumentSession();
        var backorderedOrder = await session.LoadAsync<Order>(order.Id);
        backorderedOrder!.Status.ShouldBe(OrderStatus.Backordered);
    }

    /// <summary>
    /// Test 6: FulfillmentCancelled transitions order to Cancelled and triggers RefundRequested.
    /// </summary>
    [Fact]
    public async Task FulfillmentCancelled_Triggers_Refund_And_Cancels()
    {
        // Arrange
        var order = await CreateOrderInFulfillingStatus();

        // Act
        await _fixture.ExecuteAndWaitAsync(new FulfillmentCancelled(
            order.Id, Guid.NewGuid(), "FC closed due to weather", DateTimeOffset.UtcNow));

        // Assert: Order is cancelled (payment was captured so saga stays open for RefundCompleted)
        await using var session = _fixture.GetDocumentSession();
        var cancelledOrder = await session.LoadAsync<Order>(order.Id);
        cancelledOrder!.Status.ShouldBe(OrderStatus.Cancelled);
        cancelledOrder.IsPaymentCaptured.ShouldBeTrue();
    }

    /// <summary>
    /// Test 7: Idempotency — ShipmentHandedToCarrier on already-Shipped order is a no-op.
    /// </summary>
    [Fact]
    public async Task Idempotency_ShipmentHandedToCarrier_Already_Shipped()
    {
        // Arrange: Get order to Shipped
        var order = await CreateOrderInFulfillingStatus();
        await AdvanceToShipped(order);

        // Act: Send duplicate ShipmentHandedToCarrier
        await _fixture.ExecuteAndWaitAsync(new ShipmentHandedToCarrier(
            order.Id, Guid.NewGuid(), "FedEx", "1Z999AA10123456784", DateTimeOffset.UtcNow));

        // Assert: Status unchanged, no side effects
        await using var session = _fixture.GetDocumentSession();
        var unchangedOrder = await session.LoadAsync<Order>(order.Id);
        unchangedOrder!.Status.ShouldBe(OrderStatus.Shipped);
    }

    /// <summary>
    /// Test 8: Full lifecycle with reshipment.
    /// Place → Payment → Inventory → Fulfilling → Shipped → DeliveryFailed → Reshipping → Shipped → Delivered
    /// </summary>
    [Fact]
    public async Task Full_Lifecycle_With_Reshipment()
    {
        // Arrange: Get order to Fulfilling
        var order = await CreateOrderInFulfillingStatus();

        // Step 1: ShipmentHandedToCarrier → Shipped
        var shipmentId1 = Guid.NewGuid();
        await _fixture.ExecuteAndWaitAsync(new ShipmentHandedToCarrier(
            order.Id, shipmentId1, "FedEx", "1Z999AA10123456784", DateTimeOffset.UtcNow));

        await using (var s1 = _fixture.GetDocumentSession())
        {
            var o1 = await s1.LoadAsync<Order>(order.Id);
            o1!.Status.ShouldBe(OrderStatus.Shipped);
        }

        // Step 2: ReturnToSenderInitiated → DeliveryFailed
        await _fixture.ExecuteAndWaitAsync(new ReturnToSenderInitiated(
            order.Id, shipmentId1, "FedEx", 3, 7, DateTimeOffset.UtcNow));

        await using (var s2 = _fixture.GetDocumentSession())
        {
            var o2 = await s2.LoadAsync<Order>(order.Id);
            o2!.Status.ShouldBe(OrderStatus.DeliveryFailed);
        }

        // Step 3: ReshipmentCreated → Reshipping
        var shipmentId2 = Guid.NewGuid();
        await _fixture.ExecuteAndWaitAsync(new ReshipmentCreated(
            order.Id, shipmentId1, shipmentId2, "Reshipment after RTS", DateTimeOffset.UtcNow));

        await using (var s3 = _fixture.GetDocumentSession())
        {
            var o3 = await s3.LoadAsync<Order>(order.Id);
            o3!.Status.ShouldBe(OrderStatus.Reshipping);
            o3.ActiveReshipmentShipmentId.ShouldBe(shipmentId2);
        }

        // Step 4: ShipmentHandedToCarrier again → Shipped (reshipment dispatched)
        await _fixture.ExecuteAndWaitAsync(new ShipmentHandedToCarrier(
            order.Id, shipmentId2, "UPS", "1Z999BB20123456785", DateTimeOffset.UtcNow));

        await using (var s4 = _fixture.GetDocumentSession())
        {
            var o4 = await s4.LoadAsync<Order>(order.Id);
            o4!.Status.ShouldBe(OrderStatus.Shipped);
        }

        // Step 5: ShipmentDelivered → Delivered
        await _fixture.ExecuteAndWaitAsync(new ShipmentDelivered(
            order.Id, shipmentId2, DateTimeOffset.UtcNow, "Jane Doe"));

        await using (var s5 = _fixture.GetDocumentSession())
        {
            var o5 = await s5.LoadAsync<Order>(order.Id);
            o5!.Status.ShouldBe(OrderStatus.Delivered);
        }
    }
}
