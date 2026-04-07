using Marten;
using Messages.Contracts.Fulfillment;
using Orders.Placement;
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
        var checkoutCompleted = TestFixture.CreateCheckoutCompletedMessage(
            Guid.CreateVersion7(), // OrderId
            Guid.CreateVersion7(), // CheckoutId
            customerId,
            [new CheckoutLineItem("SKU-FUL-001", 2, 15.99m)],
            new OrdersShippingAddress("123 Main St", null, "Seattle", "WA", "98101", "USA"),
            "Standard",
            5.99m, // ShippingCost
            "tok_visa",
            DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(checkoutCompleted);

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
        fulfillingOrder.TotalAmount.ShouldBe(37.97m); // 31.98 + 5.99 shipping
        fulfillingOrder.IsPaymentCaptured.ShouldBeTrue();
        fulfillingOrder.IsInventoryReserved.ShouldBeTrue();
    }

    /// <summary>
    /// Integration test: Order transitions to Shipped when ShipmentHandedToCarrier received from Fulfillment.
    /// (Migrated from ShipmentDispatched in S4)
    /// </summary>
    [Fact]
    public async Task Order_Transitions_To_Shipped_When_Shipment_HandedToCarrier()
    {
        // Arrange: Create order
        var customerId = Guid.NewGuid();
        var cartId = Guid.NewGuid();
        var checkoutCompleted = TestFixture.CreateCheckoutCompletedMessage(
            Guid.CreateVersion7(), // OrderId
            Guid.CreateVersion7(), // CheckoutId
            customerId,
            [new CheckoutLineItem("SKU-FUL-002", 1, 24.99m)],
            new OrdersShippingAddress("456 Elm St", null, "Portland", "OR", "97201", "USA"),
            "Express",
            5.99m, // ShippingCost
            "tok_visa",
            DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(checkoutCompleted);

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

        // Act: Simulate ShipmentHandedToCarrier from Fulfillment BC
        var shipmentId = Guid.NewGuid();
        await _fixture.ExecuteAndWaitAsync(new ShipmentHandedToCarrier(
            order.Id,
            shipmentId,
            "FedEx",
            "1Z999AA10123456784",
            DateTimeOffset.UtcNow));

        // Assert: Verify order transitioned to Shipped
        await using var session = _fixture.GetDocumentSession();
        var shippedOrder = await session.LoadAsync<Order>(order.Id);
        shippedOrder!.Status.ShouldBe(OrderStatus.Shipped);
        shippedOrder.TotalAmount.ShouldBe(30.98m); // 24.99 + 5.99 shipping
    }

    /// <summary>
    /// Integration test: Order transitions to Delivered when ShipmentDelivered received.
    /// Saga remains open (return window) to handle potential returns.
    /// The saga only closes when ReturnWindowExpired fires (30 days later, not tested here).
    /// </summary>
    [Fact]
    public async Task Order_Transitions_To_Delivered_When_Shipment_Delivered()
    {
        // Arrange: Create order and get it to Shipped status
        var customerId = Guid.NewGuid();
        var cartId = Guid.NewGuid();
        var checkoutCompleted = TestFixture.CreateCheckoutCompletedMessage(
            Guid.CreateVersion7(), // OrderId
            Guid.CreateVersion7(), // CheckoutId
            customerId,
            [new CheckoutLineItem("SKU-FUL-003", 3, 9.99m)],
            new OrdersShippingAddress("789 Oak Ave", null, "Denver", "CO", "80202", "USA"),
            "Standard",
            5.99m, // ShippingCost
            "tok_visa",
            DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(checkoutCompleted);

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
        await _fixture.ExecuteAndWaitAsync(new ShipmentHandedToCarrier(
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

        // Assert: Verify order transitioned to Delivered — saga remains open for the return window.
        // The saga will close (MarkCompleted) when ReturnWindowExpired fires after 30 days.
        await using var session = _fixture.GetDocumentSession();
        var deliveredOrder = await session.LoadAsync<Order>(order.Id);
        deliveredOrder.ShouldNotBeNull();
        deliveredOrder.Status.ShouldBe(OrderStatus.Delivered);
        deliveredOrder.TotalAmount.ShouldBe(35.96m); // 29.97 + 5.99 shipping
    }

    /// <summary>
    /// Regression test: Duplicate ShipmentDelivered messages must be idempotent.
    /// Under at-least-once delivery, ShipmentDelivered may arrive more than once.
    /// Without the guard, multiple ReturnWindowExpired messages would be scheduled,
    /// causing multiple saga close attempts after the return window.
    /// </summary>
    [Fact]
    public async Task ShipmentDelivered_Duplicate_Is_Idempotent()
    {
        // Arrange: Get order to Shipped status
        var customerId = Guid.NewGuid();
        var checkoutCompleted = TestFixture.CreateCheckoutCompletedMessage(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            customerId,
            [new CheckoutLineItem("SKU-FUL-005", 1, 19.99m)],
            new OrdersShippingAddress("99 Duplicate Ave", null, "Portland", "OR", "97201", "USA"),
            "Standard",
            5.99m,
            "tok_visa",
            DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(checkoutCompleted);

        Order order;
        await using (var getSession = _fixture.GetDocumentSession())
        {
            order = (await getSession.Query<Order>()
                .Where(o => o.CustomerId == customerId)
                .ToListAsync()).FirstOrDefault()!;
            order.ShouldNotBeNull();
        }

        await _fixture.ExecuteAndWaitAsync(new Messages.Contracts.Payments.PaymentCaptured(
            Guid.NewGuid(), order.Id, 25.98m, "txn_dup_test", DateTimeOffset.UtcNow));

        var reservationId = Guid.NewGuid();
        await _fixture.ExecuteAndWaitAsync(new Messages.Contracts.Inventory.ReservationConfirmed(
            order.Id, Guid.NewGuid(), reservationId, "SKU-FUL-005", "WH-01", 1, DateTimeOffset.UtcNow));

        await _fixture.ExecuteAndWaitAsync(new Messages.Contracts.Inventory.ReservationCommitted(
            order.Id, Guid.NewGuid(), reservationId, "SKU-FUL-005", "WH-01", 1, DateTimeOffset.UtcNow));

        var shipmentId = Guid.NewGuid();
        await _fixture.ExecuteAndWaitAsync(new ShipmentHandedToCarrier(
            order.Id, shipmentId, "FedEx", "774899172137", DateTimeOffset.UtcNow));

        // Act: Send ShipmentDelivered twice (duplicate)
        var delivered = new ShipmentDelivered(order.Id, shipmentId, DateTimeOffset.UtcNow, "Jane Doe");
        await _fixture.ExecuteAndWaitAsync(delivered);
        await _fixture.ExecuteAndWaitAsync(delivered); // duplicate — must be a no-op

        // Assert: Status is Delivered and saga is still open (return window active).
        // If MarkCompleted() had been called, Marten would have deleted the saga document,
        // and LoadAsync would return null — so ShouldNotBeNull() implicitly verifies the saga is alive.
        await using var querySession = _fixture.GetDocumentSession();
        var deliveredOrder = await querySession.LoadAsync<Order>(order.Id);
        deliveredOrder.ShouldNotBeNull(); // null would mean MarkCompleted() was called — it must not be
        deliveredOrder.Status.ShouldBe(OrderStatus.Delivered);
    }
}
