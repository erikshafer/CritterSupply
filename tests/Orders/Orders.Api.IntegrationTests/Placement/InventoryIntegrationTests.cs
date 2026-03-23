using Marten;
using Messages.Contracts.Inventory;
using Orders.Placement;

namespace Orders.Api.IntegrationTests.Placement;

/// <summary>
/// Integration tests for inventory-related saga handlers in Orders.
/// Tests the integration between Inventory BC and Orders BC.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class InventoryIntegrationTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public InventoryIntegrationTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.CleanAllDocumentsAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Integration test for ReservationConfirmed handler.
    /// Creates an order, sends ReservationConfirmed, verifies order transitions to InventoryReserved.
    /// **Validates: Requirement 2.1 - Order tracks inventory reservation confirmation**
    /// </summary>
    [Fact]
    public async Task ReservationConfirmed_Transitions_Order_To_InventoryReserved()
    {
        // Arrange: Create an order first
        var customerId = Guid.NewGuid();
        var lineItems = new List<CheckoutLineItem>
        {
            new("SKU-INV-001", 2, 29.99m)
        };
        var shippingAddress = new ShippingAddress(
            "123 Main St",
            null,
            "Seattle",
            "WA",
            "98101",
            "US");

        var checkoutCompleted = TestFixture.CreateCheckoutCompletedMessage(
            Guid.CreateVersion7(), // OrderId
            Guid.CreateVersion7(), // CheckoutId
            customerId,
            lineItems,
            shippingAddress,
            "Standard",
            5.99m, // ShippingCost
            "tok_test_payment",
            DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(checkoutCompleted);

        // Get the created order
        await using var session = _fixture.GetDocumentSession();
        var order = await session.Query<Order>()
            .Where(o => o.CustomerId == customerId)
            .SingleAsync();

        order.Status.ShouldBe(OrderStatus.Placed);

        // Act: Send ReservationConfirmed message
        var inventoryId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var reservationConfirmed = new ReservationConfirmed(
            order.Id,
            inventoryId,
            reservationId,
            "SKU-INV-001",
            "WH-01",
            2,
            DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(reservationConfirmed);

        // Assert: Verify order transitioned to InventoryReserved
        await using var querySession = _fixture.GetDocumentSession();
        var updatedOrder = await querySession.LoadAsync<Order>(order.Id);

        updatedOrder.ShouldNotBeNull();
        updatedOrder.Status.ShouldBe(OrderStatus.InventoryReserved);
        updatedOrder.CustomerId.ShouldBe(customerId);
        updatedOrder.TotalAmount.ShouldBe(65.97m); // 59.98 + 5.99 shipping
    }

    /// <summary>
    /// Integration test for ReservationFailed handler.
    /// Creates an order, sends ReservationFailed, verifies order transitions to OutOfStock.
    /// **Validates: Requirement 2.2 - Order fails when inventory cannot be reserved**
    /// </summary>
    [Fact]
    public async Task ReservationFailed_Transitions_Order_To_OutOfStock()
    {
        // Arrange: Create an order first
        var customerId = Guid.NewGuid();
        var lineItems = new List<CheckoutLineItem>
        {
            new("SKU-INV-002", 100, 19.99m)
        };
        var shippingAddress = new ShippingAddress(
            "456 Oak Ave",
            null,
            "Portland",
            "OR",
            "97201",
            "US");

        var checkoutCompleted = TestFixture.CreateCheckoutCompletedMessage(
            Guid.CreateVersion7(), // OrderId
            Guid.CreateVersion7(), // CheckoutId
            customerId,
            lineItems,
            shippingAddress,
            "Express",
            5.99m, // ShippingCost
            "tok_test_payment",
            DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(checkoutCompleted);

        // Get the created order
        await using var session = _fixture.GetDocumentSession();
        var order = await session.Query<Order>()
            .Where(o => o.CustomerId == customerId)
            .SingleAsync();

        order.Status.ShouldBe(OrderStatus.Placed);

        // Act: Send ReservationFailed message
        var reservationId = Guid.NewGuid();
        var reservationFailed = new ReservationFailed(
            order.Id,
            reservationId,
            "SKU-INV-002",
            "WH-01",
            100,
            10, // Only 10 available
            "Insufficient stock",
            DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(reservationFailed);

        // Assert: Verify order transitioned to InventoryFailed
        await using var querySession = _fixture.GetDocumentSession();
        var updatedOrder = await querySession.LoadAsync<Order>(order.Id);

        updatedOrder.ShouldNotBeNull();
        updatedOrder.Status.ShouldBe(OrderStatus.OutOfStock);
        updatedOrder.CustomerId.ShouldBe(customerId);
        updatedOrder.TotalAmount.ShouldBe(2004.99m); // 1999.00 + 5.99 shipping
    }

    /// <summary>
    /// Integration test for ReservationCommitted handler.
    /// Creates an order, transitions to InventoryReserved, then commits reservation.
    /// **Validates: Requirement 2.3 - Order proceeds after inventory is committed**
    /// </summary>
    [Fact]
    public async Task ReservationCommitted_Transitions_Order_To_InventoryCommitted()
    {
        // Arrange: Create an order and reserve inventory
        var customerId = Guid.NewGuid();
        var lineItems = new List<CheckoutLineItem>
        {
            new("SKU-INV-003", 3, 39.99m)
        };
        var shippingAddress = new ShippingAddress(
            "789 Elm St",
            "Apt 4B",
            "Denver",
            "CO",
            "80201",
            "US");

        var checkoutCompleted = TestFixture.CreateCheckoutCompletedMessage(
            Guid.CreateVersion7(), // OrderId
            Guid.CreateVersion7(), // CheckoutId
            customerId,
            lineItems,
            shippingAddress,
            "Standard",
            5.99m, // ShippingCost
            "tok_test_payment",
            DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(checkoutCompleted);

        // Get the created order
        await using var session = _fixture.GetDocumentSession();
        var order = await session.Query<Order>()
            .Where(o => o.CustomerId == customerId)
            .SingleAsync();

        order.Status.ShouldBe(OrderStatus.Placed);

        // Transition to InventoryReserved
        var inventoryId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var reservationConfirmed = new ReservationConfirmed(
            order.Id,
            inventoryId,
            reservationId,
            "SKU-INV-003",
            "WH-01",
            3,
            DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(reservationConfirmed);

        // Verify reservation
        await using var reservedSession = _fixture.GetDocumentSession();
        var reservedOrder = await reservedSession.LoadAsync<Order>(order.Id);
        reservedOrder!.Status.ShouldBe(OrderStatus.InventoryReserved);

        // Act: Commit reservation
        var reservationCommitted = new ReservationCommitted(
            order.Id,
            inventoryId,
            reservationId,
            "SKU-INV-003",
            "WH-01",
            3,
            DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(reservationCommitted);

        // Assert: Verify order transitioned to InventoryCommitted
        await using var querySession = _fixture.GetDocumentSession();
        var updatedOrder = await querySession.LoadAsync<Order>(order.Id);

        updatedOrder.ShouldNotBeNull();
        updatedOrder.Status.ShouldBe(OrderStatus.InventoryCommitted);
        updatedOrder.CustomerId.ShouldBe(customerId);
        updatedOrder.TotalAmount.ShouldBe(125.96m); // 119.97 + 5.99 shipping
    }

    /// <summary>
    /// Integration test for ReservationReleased handler.
    /// Creates an order, transitions to OutOfStock, then releases reservation.
    /// Reservation release is a compensation operation and doesn't change order status.
    /// **Validates: Requirement 2.4 - Order tracks inventory release for compensation**
    /// </summary>
    [Fact]
    public async Task ReservationReleased_Does_Not_Change_Order_Status()
    {
        // Arrange: Create an order and fail inventory
        var customerId = Guid.NewGuid();
        var lineItems = new List<CheckoutLineItem>
        {
            new("SKU-INV-004", 1, 99.99m)
        };
        var shippingAddress = new ShippingAddress(
            "321 Pine Ave",
            null,
            "Austin",
            "TX",
            "73301",
            "US");

        var checkoutCompleted = TestFixture.CreateCheckoutCompletedMessage(
            Guid.CreateVersion7(), // OrderId
            Guid.CreateVersion7(), // CheckoutId
            customerId,
            lineItems,
            shippingAddress,
            "Express",
            5.99m, // ShippingCost
            "tok_test_payment",
            DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(checkoutCompleted);

        // Get the created order
        await using var session = _fixture.GetDocumentSession();
        var order = await session.Query<Order>()
            .Where(o => o.CustomerId == customerId)
            .SingleAsync();

        order.Status.ShouldBe(OrderStatus.Placed);

        // Transition to OutOfStock
        var reservationId = Guid.NewGuid();
        var reservationFailed = new ReservationFailed(
            order.Id,
            reservationId,
            "SKU-INV-004",
            "WH-01",
            1,
            0, // No stock available
            "Out of stock",
            DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(reservationFailed);

        // Verify failure
        await using var failedSession = _fixture.GetDocumentSession();
        var failedOrder = await failedSession.LoadAsync<Order>(order.Id);
        failedOrder!.Status.ShouldBe(OrderStatus.OutOfStock);

        // Act: Send ReservationReleased message (compensation)
        var inventoryId = Guid.NewGuid();
        var reservationReleased = new ReservationReleased(
            order.Id,
            inventoryId,
            reservationId,
            "SKU-INV-004",
            "WH-01",
            1,
            "Compensation for failed order",
            DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(reservationReleased);

        // Assert: Verify order status remains OutOfStock (release is compensation, not state change)
        await using var querySession = _fixture.GetDocumentSession();
        var updatedOrder = await querySession.LoadAsync<Order>(order.Id);

        updatedOrder.ShouldNotBeNull();
        updatedOrder.Status.ShouldBe(OrderStatus.OutOfStock);
        updatedOrder.CustomerId.ShouldBe(customerId);
        updatedOrder.TotalAmount.ShouldBe(105.98m); // 99.99 + 5.99 shipping
    }

    /// <summary>
    /// Integration test for complex flow: reservation → payment → commitment.
    /// Simulates the happy path where inventory is reserved, payment captured, then inventory committed.
    /// </summary>
    [Fact]
    public async Task Complex_Flow_Reservation_Payment_Commitment_Succeeds()
    {
        // Arrange: Create an order
        var customerId = Guid.NewGuid();
        var lineItems = new List<CheckoutLineItem>
        {
            new("SKU-INV-005", 5, 15.99m)
        };
        var shippingAddress = new ShippingAddress(
            "555 Maple Dr",
            "Suite 200",
            "San Francisco",
            "CA",
            "94102",
            "US");

        var checkoutCompleted = TestFixture.CreateCheckoutCompletedMessage(
            Guid.CreateVersion7(), // OrderId
            Guid.CreateVersion7(), // CheckoutId
            customerId,
            lineItems,
            shippingAddress,
            "Standard",
            5.99m, // ShippingCost
            "tok_test_payment",
            DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(checkoutCompleted);

        // Get the created order
        await using var session = _fixture.GetDocumentSession();
        var order = await session.Query<Order>()
            .Where(o => o.CustomerId == customerId)
            .SingleAsync();

        // Step 1: Reserve inventory
        var inventoryId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        await _fixture.ExecuteAndWaitAsync(new ReservationConfirmed(
            order.Id,
            inventoryId,
            reservationId,
            "SKU-INV-005",
            "WH-01",
            5,
            DateTimeOffset.UtcNow));

        // Step 2: Capture payment
        await _fixture.ExecuteAndWaitAsync(new Messages.Contracts.Payments.PaymentCaptured(
            Guid.NewGuid(),
            order.Id,
            order.TotalAmount,
            "txn_12345",
            DateTimeOffset.UtcNow));

        // Step 3: Commit inventory
        await _fixture.ExecuteAndWaitAsync(new ReservationCommitted(
            order.Id,
            inventoryId,
            reservationId,
            "SKU-INV-005",
            "WH-01",
            5,
            DateTimeOffset.UtcNow));

        // Assert: Verify final state - now transitions to Fulfilling after both payment and inventory confirmed
        await using var querySession = _fixture.GetDocumentSession();
        var finalOrder = await querySession.LoadAsync<Order>(order.Id);

        finalOrder.ShouldNotBeNull();
        finalOrder.Status.ShouldBe(OrderStatus.Fulfilling); // Orchestration complete - ready for fulfillment
        finalOrder.TotalAmount.ShouldBe(85.94m); // 79.95 + 5.99 shipping
        finalOrder.IsPaymentCaptured.ShouldBeTrue();
        finalOrder.IsInventoryReserved.ShouldBeTrue();
    }

    /// <summary>
    /// Regression test: Duplicate ReservationConfirmed for the same ReservationId must be idempotent.
    /// Under at-least-once delivery, the same message may arrive twice. Without the guard,
    /// ConfirmedReservationCount would increment twice for one SKU, causing IsInventoryReserved
    /// to trigger prematurely and potentially sending duplicate ReservationCommitRequested messages.
    /// **Validates: Requirement 2.1 idempotency under at-least-once delivery**
    /// </summary>
    [Fact]
    public async Task ReservationConfirmed_Duplicate_Is_Idempotent()
    {
        // Arrange: Create a two-SKU order
        var customerId = Guid.NewGuid();
        var lineItems = new List<CheckoutLineItem>
        {
            new("SKU-IDEM-001", 1, 10.00m),
            new("SKU-IDEM-002", 1, 20.00m)
        };
        var checkoutCompleted = TestFixture.CreateCheckoutCompletedMessage(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            customerId,
            lineItems,
            new ShippingAddress("1 Idempotency Lane", null, "Boston", "MA", "02101", "US"),
            "Standard",
            5.00m,
            "tok_idem_test",
            DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(checkoutCompleted);

        await using var session = _fixture.GetDocumentSession();
        var order = await session.Query<Order>()
            .Where(o => o.CustomerId == customerId)
            .SingleAsync();

        order.ExpectedReservationCount.ShouldBe(2);

        // Act: Send ReservationConfirmed for SKU-IDEM-001 twice (duplicate delivery)
        var inventoryId = Guid.NewGuid();
        var reservationId1 = Guid.NewGuid();
        var confirmed = new ReservationConfirmed(
            order.Id, inventoryId, reservationId1, "SKU-IDEM-001", "WH-01", 1, DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(confirmed);
        await _fixture.ExecuteAndWaitAsync(confirmed); // duplicate

        // Assert: ConfirmedReservationCount must remain 1 (not 2), status still Placed (not InventoryReserved)
        await using var querySession = _fixture.GetDocumentSession();
        var updatedOrder = await querySession.LoadAsync<Order>(order.Id);
        updatedOrder.ShouldNotBeNull();
        updatedOrder.ConfirmedReservationCount.ShouldBe(1);
        updatedOrder.Status.ShouldBe(OrderStatus.Placed); // NOT InventoryReserved yet — only 1 of 2 SKUs
    }

    /// <summary>
    /// Regression test for the multi-SKU payment-between-reservations race condition.
    /// Ordering: R1 confirmed → payment captured → R2 confirmed.
    ///
    /// With the bug (before fix): HandlePaymentCaptured sees IsInventoryReserved=false (only 1/2 confirmed),
    /// emits no commits. HandleReservationConfirmed for R2 emits commit for R2 only; R1 is never committed.
    /// The order gets permanently stuck.
    ///
    /// With the fix: HandleReservationConfirmed (IsPaymentCaptured=true) emits commits for ALL
    /// confirmed reservations. HandleReservationCommitted is idempotent via CommittedReservationIds.
    ///
    /// **Validates: Requirement 2.3 (multi-SKU) — all SKUs reach Fulfilling despite interleaved payment**
    /// </summary>
    [Fact]
    public async Task MultiSku_PaymentBetweenReservations_AllReservationsCommitted()
    {
        // Arrange: Two-SKU order
        var customerId = Guid.NewGuid();
        var lineItems = new List<CheckoutLineItem>
        {
            new("SKU-RACE-001", 2, 25.00m),
            new("SKU-RACE-002", 1, 50.00m)
        };
        var checkoutCompleted = TestFixture.CreateCheckoutCompletedMessage(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            customerId,
            lineItems,
            new ShippingAddress("10 Race Condition Blvd", null, "Concurrency City", "TX", "75001", "US"),
            "Standard",
            9.99m,
            "tok_race_test",
            DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(checkoutCompleted);

        await using var session = _fixture.GetDocumentSession();
        var order = await session.Query<Order>()
            .Where(o => o.CustomerId == customerId)
            .SingleAsync();

        order.ExpectedReservationCount.ShouldBe(2);

        // Step 1: Only R1 (SKU-RACE-001) confirmed — payment NOT yet captured
        var inventoryId = Guid.NewGuid();
        var reservationId1 = Guid.NewGuid();
        await _fixture.ExecuteAndWaitAsync(new ReservationConfirmed(
            order.Id, inventoryId, reservationId1, "SKU-RACE-001", "WH-01", 2, DateTimeOffset.UtcNow));

        await using var afterR1Session = _fixture.GetDocumentSession();
        var afterR1 = await afterR1Session.LoadAsync<Order>(order.Id);
        afterR1!.ConfirmedReservationCount.ShouldBe(1);
        afterR1.IsInventoryReserved.ShouldBeFalse();

        // Step 2: Payment captured BEFORE R2 confirmed — IsInventoryReserved is still false
        await _fixture.ExecuteAndWaitAsync(new Messages.Contracts.Payments.PaymentCaptured(
            Guid.NewGuid(), order.Id, order.TotalAmount, "txn_race_001", DateTimeOffset.UtcNow));

        await using var afterPaySession = _fixture.GetDocumentSession();
        var afterPay = await afterPaySession.LoadAsync<Order>(order.Id);
        afterPay!.IsPaymentCaptured.ShouldBeTrue();
        afterPay.IsInventoryReserved.ShouldBeFalse(); // Still only 1/2

        // Step 3: R2 (SKU-RACE-002) confirmed AFTER payment.
        // The fix: commit requests are issued for BOTH R1 and R2.
        var reservationId2 = Guid.NewGuid();
        await _fixture.ExecuteAndWaitAsync(new ReservationConfirmed(
            order.Id, inventoryId, reservationId2, "SKU-RACE-002", "WH-01", 1, DateTimeOffset.UtcNow));

        // Step 4: Both R1 and R2 committed (saga is idempotent for duplicate commits)
        await _fixture.ExecuteAndWaitAsync(new ReservationCommitted(
            order.Id, inventoryId, reservationId1, "SKU-RACE-001", "WH-01", 2, DateTimeOffset.UtcNow));

        await _fixture.ExecuteAndWaitAsync(new ReservationCommitted(
            order.Id, inventoryId, reservationId2, "SKU-RACE-002", "WH-01", 1, DateTimeOffset.UtcNow));

        // Assert: Both SKUs committed → order proceeds to Fulfilling (not stuck)
        await using var querySession = _fixture.GetDocumentSession();
        var finalOrder = await querySession.LoadAsync<Order>(order.Id);

        finalOrder.ShouldNotBeNull();
        finalOrder.Status.ShouldBe(OrderStatus.Fulfilling);
        finalOrder.CommittedReservationCount.ShouldBe(2);
        finalOrder.CommittedReservationIds.ShouldContain(reservationId1);
        finalOrder.CommittedReservationIds.ShouldContain(reservationId2);
        finalOrder.IsPaymentCaptured.ShouldBeTrue();
        finalOrder.IsAllInventoryCommitted.ShouldBeTrue();
        finalOrder.TotalAmount.ShouldBe(109.99m); // (2×25 + 1×50) + 9.99 shipping
    }
}
