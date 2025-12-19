using Marten;
using Messages.Contracts.Fulfillment;
using Orders.Placement;
using Shouldly;
using OrdersShippingAddress = Orders.Placement.ShippingAddress;

namespace Orders.Api.IntegrationTests.Placement;

/// <summary>
/// Integration tests for Orders ↔ Fulfillment BC integration.
/// Tests the complete flow: Payment + Inventory confirmed → Fulfillment requested → Shipment status updates.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class FulfillmentIntegrationTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public FulfillmentIntegrationTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.CleanAllDocumentsAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Integration test: Orders publishes FulfillmentRequested after both payment and inventory confirmed.
    /// Validates orchestration pattern - Orders coordinates fulfillment timing.
    /// </summary>
    [Fact]
    public async Task FulfillmentRequested_Published_After_Payment_And_Inventory_Confirmed()
    {
        // Arrange: Create an order
        var customerId = Guid.NewGuid();
        var cartId = Guid.NewGuid();
        var checkout = new CheckoutCompleted(
            cartId,
            customerId,
            [new CheckoutLineItem("SKU-FUL-001", 2, 15.99m)],
            new OrdersShippingAddress("123 Main St", null, "Seattle", "WA", "98101", "USA"),
            "Standard",
            "tok_visa",
            null,
            DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(checkout);

        // Get the created order
        await using var getSession = _fixture.GetDocumentSession();
        var order = (await getSession.Query<Order>()
            .Where(o => o.CustomerId == customerId)
            .ToListAsync()).FirstOrDefault();
        order.ShouldNotBeNull();

        // Act 1: Simulate payment capture
        await _fixture.ExecuteAndWaitAsync(new Messages.Contracts.Payments.PaymentCaptured(
            Guid.NewGuid(),
            order.Id,
            31.98m,
            "txn_12345",
            DateTimeOffset.UtcNow));

        // Act 2: Simulate inventory reservation confirmed
        var reservationId = Guid.NewGuid();
        await _fixture.ExecuteAndWaitAsync(new Messages.Contracts.Inventory.ReservationConfirmed(
            order.Id,
            Guid.NewGuid(),
            reservationId,
            "SKU-FUL-001",
            "WH-01",
            2,
            DateTimeOffset.UtcNow));

        // Act 3: Simulate inventory commitment (hard allocation)
        await _fixture.ExecuteAndWaitAsync(new Messages.Contracts.Inventory.ReservationCommitted(
            order.Id,
            Guid.NewGuid(),
            reservationId,
            "SKU-FUL-001",
            "WH-01",
            2,
            DateTimeOffset.UtcNow));

        // Assert: Verify order status transitioned to Fulfilling (which proves FulfillmentRequested was published)
        await using var session = _fixture.GetDocumentSession();
        var fulfillingOrder = await session.LoadAsync<Order>(order.Id);
        fulfillingOrder.ShouldNotBeNull();
        fulfillingOrder.Status.ShouldBe(OrderStatus.Fulfilling);
        fulfillingOrder.IsPaymentCaptured.ShouldBeTrue();
        fulfillingOrder.IsInventoryReserved.ShouldBeTrue();
    }

    /// <summary>
    /// Integration test: Order transitions to Shipped when ShipmentDispatched received from Fulfillment.
    /// </summary>
    [Fact]
    public async Task Order_Transitions_To_Shipped_When_Shipment_Dispatched()
    {
        // Arrange: Create order
        var customerId = Guid.NewGuid();
        var cartId = Guid.NewGuid();
        var checkout = new CheckoutCompleted(
            cartId,
            customerId,
            [new CheckoutLineItem("SKU-FUL-002", 1, 24.99m)],
            new OrdersShippingAddress("456 Elm St", null, "Portland", "OR", "97201", "USA"),
            "Express",
            "tok_visa",
            null,
            DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(checkout);

        await using var getSession = _fixture.GetDocumentSession();
        var order = (await getSession.Query<Order>()
            .Where(o => o.CustomerId == customerId)
            .ToListAsync()).FirstOrDefault();
        order.ShouldNotBeNull();

        // Simulate payment + inventory flow to get to Fulfilling status
        await _fixture.ExecuteAndWaitAsync(new Messages.Contracts.Payments.PaymentCaptured(
            Guid.NewGuid(), order.Id, 24.99m, "txn_67890", DateTimeOffset.UtcNow));

        var reservationId = Guid.NewGuid();
        await _fixture.ExecuteAndWaitAsync(new Messages.Contracts.Inventory.ReservationConfirmed(
            order.Id, Guid.NewGuid(), reservationId, "SKU-FUL-002", "WH-01", 1, DateTimeOffset.UtcNow));

        await _fixture.ExecuteAndWaitAsync(new Messages.Contracts.Inventory.ReservationCommitted(
            order.Id, Guid.NewGuid(), reservationId, "SKU-FUL-002", "WH-01", 1, DateTimeOffset.UtcNow));

        // Act: Simulate ShipmentDispatched from Fulfillment BC
        var shipmentId = Guid.NewGuid();
        await _fixture.ExecuteAndWaitAsync(new ShipmentDispatched(
            order.Id,
            shipmentId,
            "FedEx",
            "1Z999AA10123456784",
            DateTimeOffset.UtcNow));

        // Assert: Verify order transitioned to Shipped
        await using var session = _fixture.GetDocumentSession();
        var shippedOrder = await session.LoadAsync<Order>(order.Id);
        shippedOrder.Status.ShouldBe(OrderStatus.Shipped);
    }

    /// <summary>
    /// Integration test: Order transitions to Delivered and saga completes when ShipmentDelivered received.
    /// </summary>
    [Fact]
    public async Task Order_Transitions_To_Delivered_And_Saga_Completes_When_Shipment_Delivered()
    {
        // Arrange: Create order and get it to Shipped status
        var customerId = Guid.NewGuid();
        var cartId = Guid.NewGuid();
        var checkout = new CheckoutCompleted(
            cartId,
            customerId,
            [new CheckoutLineItem("SKU-FUL-003", 3, 9.99m)],
            new OrdersShippingAddress("789 Oak Ave", null, "Denver", "CO", "80202", "USA"),
            "Standard",
            "tok_visa",
            null,
            DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(checkout);

        Order order;
        await using (var getSession = _fixture.GetDocumentSession())
        {
            order = (await getSession.Query<Order>()
                .Where(o => o.CustomerId == customerId)
                .ToListAsync()).FirstOrDefault()!;
            order.ShouldNotBeNull();
        }

        // Simulate payment + inventory + dispatch flow
        await _fixture.ExecuteAndWaitAsync(new Messages.Contracts.Payments.PaymentCaptured(
            Guid.NewGuid(), order.Id, 29.97m, "txn_abc123", DateTimeOffset.UtcNow));

        var reservationId = Guid.NewGuid();
        await _fixture.ExecuteAndWaitAsync(new Messages.Contracts.Inventory.ReservationConfirmed(
            order.Id, Guid.NewGuid(), reservationId, "SKU-FUL-003", "WH-01", 3, DateTimeOffset.UtcNow));

        await _fixture.ExecuteAndWaitAsync(new Messages.Contracts.Inventory.ReservationCommitted(
            order.Id, Guid.NewGuid(), reservationId, "SKU-FUL-003", "WH-01", 3, DateTimeOffset.UtcNow));

        var shipmentId = Guid.NewGuid();
        await _fixture.ExecuteAndWaitAsync(new ShipmentDispatched(
            order.Id, shipmentId, "UPS", "1Z999AA10123456785", DateTimeOffset.UtcNow));

        // Verify order is in Shipped status before delivery
        await using (var preDeliverySession = _fixture.GetDocumentSession())
        {
            var shippedOrder = await preDeliverySession.LoadAsync<Order>(order.Id);
            shippedOrder.ShouldNotBeNull();
            shippedOrder.Status.ShouldBe(OrderStatus.Shipped);
        }

        // Act: Simulate ShipmentDelivered from Fulfillment BC
        await _fixture.ExecuteAndWaitAsync(new ShipmentDelivered(
            order.Id,
            shipmentId,
            DateTimeOffset.UtcNow,
            "John Doe"));

        // Assert: Verify saga was completed and deleted (MarkCompleted() deletes the saga document)
        await using var session = _fixture.GetDocumentSession();
        var deliveredOrder = await session.LoadAsync<Order>(order.Id);
        deliveredOrder.ShouldBeNull(); // Saga is deleted after MarkCompleted() is called
    }

    /// <summary>
    /// Integration test: Order status remains Shipped when delivery fails.
    /// Validates that delivery failure doesn't cause backward state transition.
    /// </summary>
    [Fact]
    public async Task Order_Remains_Shipped_When_Delivery_Fails()
    {
        // Arrange: Create order and get it to Shipped status
        var customerId = Guid.NewGuid();
        var cartId = Guid.NewGuid();
        var checkout = new CheckoutCompleted(
            cartId,
            customerId,
            [new CheckoutLineItem("SKU-FUL-004", 1, 49.99m)],
            new OrdersShippingAddress("321 Pine St", "Apt 5B", "Austin", "TX", "78701", "USA"),
            "Express",
            "tok_visa",
            null,
            DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(checkout);

        await using var getSession = _fixture.GetDocumentSession();
        var order = (await getSession.Query<Order>()
            .Where(o => o.CustomerId == customerId)
            .ToListAsync()).FirstOrDefault();
        order.ShouldNotBeNull();

        // Simulate payment + inventory + dispatch flow
        await _fixture.ExecuteAndWaitAsync(new Messages.Contracts.Payments.PaymentCaptured(
            Guid.NewGuid(), order.Id, 49.99m, "txn_xyz789", DateTimeOffset.UtcNow));

        var reservationId = Guid.NewGuid();
        await _fixture.ExecuteAndWaitAsync(new Messages.Contracts.Inventory.ReservationConfirmed(
            order.Id, Guid.NewGuid(), reservationId, "SKU-FUL-004", "WH-01", 1, DateTimeOffset.UtcNow));

        await _fixture.ExecuteAndWaitAsync(new Messages.Contracts.Inventory.ReservationCommitted(
            order.Id, Guid.NewGuid(), reservationId, "SKU-FUL-004", "WH-01", 1, DateTimeOffset.UtcNow));

        var shipmentId = Guid.NewGuid();
        await _fixture.ExecuteAndWaitAsync(new ShipmentDispatched(
            order.Id, shipmentId, "USPS", "9400111899562537986459", DateTimeOffset.UtcNow));

        // Act: Simulate delivery failure from Fulfillment BC
        await _fixture.ExecuteAndWaitAsync(new ShipmentDeliveryFailed(
            order.Id,
            shipmentId,
            "Recipient unavailable - notice left",
            DateTimeOffset.UtcNow));

        // Assert: Verify order remains in Shipped status
        await using var session = _fixture.GetDocumentSession();
        var shippedOrder = await session.LoadAsync<Order>(order.Id);
        shippedOrder.Status.ShouldBe(OrderStatus.Shipped);

        // Future enhancement: Verify delivery failure metadata is tracked
    }
}
