using Marten;
using Messages.Contracts.Inventory;
using Messages.Contracts.Payments;
using Orders.Placement;
using OrdersShippingAddress = Orders.Placement.ShippingAddress;

namespace Orders.Api.IntegrationTests.Placement;

/// <summary>
/// Integration tests for order cancellation flows.
/// Tests the CancelOrder command, guards, and compensation logic.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class CancellationIntegrationTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public CancellationIntegrationTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.CleanAllDocumentsAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Integration test: Order can be cancelled when in Placed status (no payment captured).
    /// Verifies the saga is immediately closed (MarkCompleted) since there is no refund to await.
    /// Late-arriving ReservationReleased messages will be silently discarded by Wolverine.
    /// </summary>
    [Fact]
    public async Task Order_Can_Be_Cancelled_When_Placed()
    {
        // Arrange: Create an order
        var customerId = Guid.NewGuid();
        var checkoutCompleted = TestFixture.CreateCheckoutCompletedMessage(customerId);

        await _fixture.ExecuteAndWaitAsync(checkoutCompleted);

        await using var getSession = _fixture.GetDocumentSession();
        var order = await getSession.Query<Order>()
            .Where(o => o.CustomerId == customerId)
            .SingleAsync();

        order.Status.ShouldBe(OrderStatus.Placed);

        // Act: Cancel the order (no payment captured)
        await _fixture.ExecuteAndWaitAsync(new CancelOrder(order.Id, "Customer changed their mind"));

        // Assert: Saga is immediately completed (document deleted) — no refund to await.
        // MarkCompleted() is called in Handle(CancelOrder) when !IsPaymentCaptured.
        await using var session = _fixture.GetDocumentSession();
        var cancelledOrder = await session.LoadAsync<Order>(order.Id);

        cancelledOrder.ShouldBeNull(); // Saga deleted by MarkCompleted() — no refund pending
    }

    /// <summary>
    /// Integration test: Order can be cancelled after payment captured, releasing inventory
    /// and triggering a refund.
    /// </summary>
    [Fact]
    public async Task Order_Cancellation_After_Payment_Triggers_Refund()
    {
        // Arrange: Create an order and capture payment
        var customerId = Guid.NewGuid();
        var checkoutCompleted = TestFixture.CreateCheckoutCompletedMessage(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            customerId,
            [new CheckoutLineItem("SKU-CANCEL-001", 2, 25.00m)],
            new OrdersShippingAddress("100 Cancel St", null, "Boston", "MA", "02101", "USA"),
            "Standard",
            5.99m,
            "tok_visa",
            DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(checkoutCompleted);

        await using var getSession = _fixture.GetDocumentSession();
        var order = await getSession.Query<Order>()
            .Where(o => o.CustomerId == customerId)
            .SingleAsync();

        // Capture payment
        await _fixture.ExecuteAndWaitAsync(new PaymentCaptured(
            Guid.NewGuid(),
            order.Id,
            55.99m,
            "txn_cancel_test",
            DateTimeOffset.UtcNow));

        // Also confirm inventory reservation
        var reservationId = Guid.NewGuid();
        await _fixture.ExecuteAndWaitAsync(new ReservationConfirmed(
            order.Id,
            Guid.NewGuid(),
            reservationId,
            "SKU-CANCEL-001",
            "WH-01",
            2,
            DateTimeOffset.UtcNow));

        // Verify state before cancellation
        await using var beforeCancelSession = _fixture.GetDocumentSession();
        var confirmedOrder = await beforeCancelSession.LoadAsync<Order>(order.Id);
        confirmedOrder.ShouldNotBeNull();
        confirmedOrder.IsPaymentCaptured.ShouldBeTrue();
        confirmedOrder.IsInventoryReserved.ShouldBeTrue();

        // Act: Cancel the order
        await _fixture.ExecuteAndWaitAsync(new CancelOrder(order.Id, "Customer cancelled"));

        // Assert: Order transitions to Cancelled
        await using var session = _fixture.GetDocumentSession();
        var cancelledOrder = await session.LoadAsync<Order>(order.Id);

        cancelledOrder.ShouldNotBeNull();
        cancelledOrder.Status.ShouldBe(OrderStatus.Cancelled);
        cancelledOrder.IsPaymentCaptured.ShouldBeTrue(); // Payment was captured before cancel
    }

    /// <summary>
    /// Integration test: Cancellation guard prevents cancelling a shipped order.
    /// </summary>
    [Fact]
    public async Task Order_Cannot_Be_Cancelled_After_Shipped()
    {
        // Arrange: Create an order and get it to Shipped status
        var customerId = Guid.NewGuid();
        var checkoutCompleted = TestFixture.CreateCheckoutCompletedMessage(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            customerId,
            [new CheckoutLineItem("SKU-NOCANCEL-001", 1, 49.99m)],
            new OrdersShippingAddress("200 Ship St", null, "Chicago", "IL", "60601", "USA"),
            "Express",
            5.99m,
            "tok_visa",
            DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(checkoutCompleted);

        await using var getSession = _fixture.GetDocumentSession();
        var order = await getSession.Query<Order>()
            .Where(o => o.CustomerId == customerId)
            .SingleAsync();

        // Simulate full flow to Shipped
        await _fixture.ExecuteAndWaitAsync(new PaymentCaptured(
            Guid.NewGuid(), order.Id, 55.98m, "txn_nocancel", DateTimeOffset.UtcNow));

        var reservationId = Guid.NewGuid();
        await _fixture.ExecuteAndWaitAsync(new ReservationConfirmed(
            order.Id, Guid.NewGuid(), reservationId, "SKU-NOCANCEL-001", "WH-01", 1, DateTimeOffset.UtcNow));

        await _fixture.ExecuteAndWaitAsync(new ReservationCommitted(
            order.Id, Guid.NewGuid(), reservationId, "SKU-NOCANCEL-001", "WH-01", 1, DateTimeOffset.UtcNow));

        await _fixture.ExecuteAndWaitAsync(new Messages.Contracts.Fulfillment.ShipmentDispatched(
            order.Id, Guid.NewGuid(), "FedEx", "TRACKING_NUM", DateTimeOffset.UtcNow));

        // Verify the order is Shipped
        await using var shippedSession = _fixture.GetDocumentSession();
        var shippedOrder = await shippedSession.LoadAsync<Order>(order.Id);
        shippedOrder!.Status.ShouldBe(OrderStatus.Shipped);

        // Act: Attempt to cancel — saga handler silently ignores invalid state transitions.
        // The HTTP endpoint would return 409, but here we test saga-level idempotency by
        // publishing the command directly (bypassing the HTTP layer).
        await _fixture.ExecuteAndWaitAsync(new CancelOrder(order.Id, "Too late to cancel"));

        // Assert: Order status is still Shipped (cancellation was rejected)
        await using var session = _fixture.GetDocumentSession();
        var unchangedOrder = await session.LoadAsync<Order>(order.Id);

        unchangedOrder.ShouldNotBeNull();
        unchangedOrder.Status.ShouldBe(OrderStatus.Shipped);
    }

    /// <summary>
    /// Integration test: RefundCompleted after cancellation closes the order lifecycle.
    /// When the payment refund is processed, the saga transitions to Closed and completes.
    /// </summary>
    [Fact]
    public async Task RefundCompleted_After_Cancellation_Closes_Order()
    {
        // Arrange: Create and cancel an order that had payment captured
        var customerId = Guid.NewGuid();
        var checkoutCompleted = TestFixture.CreateCheckoutCompletedMessage(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            customerId,
            [new CheckoutLineItem("SKU-REFUND-001", 1, 79.99m)],
            new OrdersShippingAddress("300 Refund Ave", null, "Miami", "FL", "33101", "USA"),
            "Standard",
            5.99m,
            "tok_visa",
            DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(checkoutCompleted);

        await using var getSession = _fixture.GetDocumentSession();
        var order = await getSession.Query<Order>()
            .Where(o => o.CustomerId == customerId)
            .SingleAsync();

        // Capture payment
        await _fixture.ExecuteAndWaitAsync(new PaymentCaptured(
            Guid.NewGuid(), order.Id, 85.98m, "txn_refund", DateTimeOffset.UtcNow));

        // Cancel the order (triggers RefundRequested to Payments BC)
        await _fixture.ExecuteAndWaitAsync(new CancelOrder(order.Id, "Customer cancelled after payment"));

        await using var cancelledSession = _fixture.GetDocumentSession();
        var cancelledOrder = await cancelledSession.LoadAsync<Order>(order.Id);
        cancelledOrder!.Status.ShouldBe(OrderStatus.Cancelled);

        // Act: Simulate Payments BC responding that refund completed
        await _fixture.ExecuteAndWaitAsync(new RefundCompleted(
            Guid.NewGuid(),
            order.Id,
            85.98m,
            "refund_txn_12345",
            DateTimeOffset.UtcNow));

        // Assert: Order transitions to Closed and saga is marked complete (deleted)
        await using var session = _fixture.GetDocumentSession();
        var closedOrder = await session.LoadAsync<Order>(order.Id);

        // Saga is deleted by MarkCompleted() when it transitions to Closed
        closedOrder.ShouldBeNull();
    }

    /// <summary>
    /// Regression test (Bug 8.2): RefundCompleted after OutOfStock closes the saga.
    /// When inventory fails after payment capture, RefundRequested is issued. When the Payments BC
    /// responds with RefundCompleted, the saga (in OutOfStock status) must transition to Closed
    /// and call MarkCompleted(), just like the Cancelled path. Previously, HandleRefundCompleted
    /// only handled Cancelled status — OutOfStock sagas were orphaned forever.
    /// </summary>
    [Fact]
    public async Task RefundCompleted_After_OutOfStock_Closes_Saga()
    {
        // Arrange: Create an order with payment captured, then fail inventory
        var customerId = Guid.NewGuid();
        var checkoutCompleted = TestFixture.CreateCheckoutCompletedMessage(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            customerId,
            [new CheckoutLineItem("SKU-OOS-REFUND-001", 999, 9.99m)],
            new OrdersShippingAddress("10 OutOfStock Rd", null, "Dallas", "TX", "75201", "USA"),
            "Standard",
            5.99m,
            "tok_visa",
            DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(checkoutCompleted);

        await using var getSession = _fixture.GetDocumentSession();
        var order = await getSession.Query<Order>()
            .Where(o => o.CustomerId == customerId)
            .SingleAsync();

        // Capture payment before inventory fails
        await _fixture.ExecuteAndWaitAsync(new PaymentCaptured(
            Guid.NewGuid(), order.Id, 9996.01m, "txn_oos", DateTimeOffset.UtcNow));

        // Fail the inventory reservation (triggers RefundRequested since payment was captured)
        await _fixture.ExecuteAndWaitAsync(new ReservationFailed(
            order.Id, Guid.NewGuid(), "SKU-OOS-REFUND-001", "WH-01", 999, 0, "No stock", DateTimeOffset.UtcNow));

        await using var oosSession = _fixture.GetDocumentSession();
        var oosOrder = await oosSession.LoadAsync<Order>(order.Id);
        oosOrder.ShouldNotBeNull();
        oosOrder.Status.ShouldBe(OrderStatus.OutOfStock);

        // Act: Simulate Payments BC completing the refund
        await _fixture.ExecuteAndWaitAsync(new RefundCompleted(
            Guid.NewGuid(), order.Id, 9996.01m, "refund_oos_001", DateTimeOffset.UtcNow));

        // Assert: Saga transitions to Closed and is deleted by MarkCompleted()
        await using var querySession = _fixture.GetDocumentSession();
        var closedOrder = await querySession.LoadAsync<Order>(order.Id);
        closedOrder.ShouldBeNull(); // null = saga deleted; previously would have been stuck in OutOfStock
    }

    /// <summary>
    /// Regression test (Bug 8.3): A late ReservationConfirmed arriving after cancellation is silently ignored.
    /// When IsPaymentCaptured=true and the order is already Cancelled, the old code would issue a
    /// ReservationCommitRequested message — directing Inventory BC to hard-allocate stock for a cancelled
    /// order that already had its inventory release requested. The terminal-state guard prevents this.
    /// </summary>
    [Fact]
    public async Task Late_ReservationConfirmed_After_Cancellation_Is_Ignored()
    {
        // Arrange: Create an order, capture payment, then cancel it
        var customerId = Guid.NewGuid();
        var checkoutCompleted = TestFixture.CreateCheckoutCompletedMessage(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            customerId,
            [new CheckoutLineItem("SKU-LATE-001", 1, 49.99m)],
            new OrdersShippingAddress("99 Late Message Lane", null, "Seattle", "WA", "98101", "USA"),
            "Standard",
            5.99m,
            "tok_visa",
            DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(checkoutCompleted);

        await using var getSession = _fixture.GetDocumentSession();
        var order = await getSession.Query<Order>()
            .Where(o => o.CustomerId == customerId)
            .SingleAsync();

        // Capture payment before cancellation
        await _fixture.ExecuteAndWaitAsync(new PaymentCaptured(
            Guid.NewGuid(), order.Id, 55.98m, "txn_late_test", DateTimeOffset.UtcNow));

        // Cancel the order (with payment — saga stays in Cancelled awaiting RefundCompleted)
        await _fixture.ExecuteAndWaitAsync(new CancelOrder(order.Id, "Customer cancelled"));

        await using var cancelledSession = _fixture.GetDocumentSession();
        var cancelledOrder = await cancelledSession.LoadAsync<Order>(order.Id);
        cancelledOrder!.Status.ShouldBe(OrderStatus.Cancelled);

        // Act: A late-arriving ReservationConfirmed from Inventory BC (message was in-flight during cancel)
        // With IsPaymentCaptured=true, the old code would have issued ReservationCommitRequested here.
        // The terminal-state guard must silently ignore this message.
        var reservationId = Guid.NewGuid();
        await _fixture.ExecuteAndWaitAsync(new ReservationConfirmed(
            order.Id, Guid.NewGuid(), reservationId, "SKU-LATE-001", "WH-01", 1, DateTimeOffset.UtcNow));

        // Assert: Order remains Cancelled, ConfirmedReservationCount unchanged (no spurious commit issued)
        await using var querySession = _fixture.GetDocumentSession();
        var afterLateMessage = await querySession.LoadAsync<Order>(order.Id);
        afterLateMessage.ShouldNotBeNull();
        afterLateMessage.Status.ShouldBe(OrderStatus.Cancelled); // Status unchanged
        afterLateMessage.ConfirmedReservationCount.ShouldBe(0);  // No count inflation
    }
}
