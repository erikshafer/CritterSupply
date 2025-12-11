using Marten;
using Messages.Contracts.Payments;
using Orders.Placement;

namespace Orders.Api.IntegrationTests.Placement;

/// <summary>
/// Integration tests for payment-related saga handlers in Orders.
/// Tests the integration between Payments BC and Orders BC.
/// </summary>
[Collection("orders-integration")]
public class PaymentIntegrationTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public PaymentIntegrationTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.CleanAllDocumentsAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Integration test for PaymentCaptured handler.
    /// Creates an order, sends PaymentCapturedIntegration, verifies order transitions to PaymentConfirmed.
    /// **Validates: Requirement 1.2 - Order proceeds after payment confirmation**
    /// </summary>
    [Fact]
    public async Task PaymentCapturedIntegration_Transitions_Order_To_PaymentConfirmed()
    {
        // Arrange: Create an order first
        var customerId = Guid.NewGuid();
        var lineItems = new List<CheckoutLineItem>
        {
            new("SKU-001", 2, 19.99m)
        };
        var shippingAddress = new ShippingAddress(
            "123 Main St",
            null,
            "Springfield",
            "IL",
            "62701",
            "US");

        var checkoutCommand = new CheckoutCompleted(
            Guid.NewGuid(), // CartId
            customerId,
            lineItems,
            shippingAddress,
            "Standard",
            "tok_test_payment",
            null, // AppliedDiscounts
            DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(checkoutCommand);

        // Get the created order
        await using var session = _fixture.GetDocumentSession();
        var order = await session.Query<Order>()
            .Where(o => o.CustomerId == customerId)
            .SingleAsync();

        order.Status.ShouldBe(OrderStatus.Placed);

        // Act: Send PaymentCaptured message
        var paymentCaptured = new PaymentCaptured(
            Guid.NewGuid(),
            order.Id,
            order.TotalAmount,
            "txn_12345",
            DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(paymentCaptured);

        // Assert: Verify order transitioned to PaymentConfirmed
        await using var querySession = _fixture.GetDocumentSession();
        var updatedOrder = await querySession.LoadAsync<Order>(order.Id);

        updatedOrder.ShouldNotBeNull();
        updatedOrder.Status.ShouldBe(OrderStatus.PaymentConfirmed);
        updatedOrder.CustomerId.ShouldBe(customerId);
        updatedOrder.TotalAmount.ShouldBe(39.98m);
    }

    /// <summary>
    /// Integration test for PaymentFailed handler.
    /// Creates an order, sends PaymentFailedIntegration, verifies order transitions to PaymentFailed.
    /// **Validates: Requirement 1.3 - Order fails when payment cannot be processed**
    /// </summary>
    [Fact]
    public async Task PaymentFailedIntegration_Transitions_Order_To_PaymentFailed()
    {
        // Arrange: Create an order first
        var customerId = Guid.NewGuid();
        var lineItems = new List<CheckoutLineItem>
        {
            new("SKU-002", 1, 49.99m)
        };
        var shippingAddress = new ShippingAddress(
            "456 Oak Ave",
            null,
            "Portland",
            "OR",
            "97201",
            "US");

        var checkoutCommand = new CheckoutCompleted(
            Guid.NewGuid(), // CartId
            customerId,
            lineItems,
            shippingAddress,
            "Express",
            "tok_test_payment",
            null, // AppliedDiscounts
            DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(checkoutCommand);

        // Get the created order
        await using var session = _fixture.GetDocumentSession();
        var order = await session.Query<Order>()
            .Where(o => o.CustomerId == customerId)
            .SingleAsync();

        order.Status.ShouldBe(OrderStatus.Placed);

        // Act: Send PaymentFailed message
        var paymentFailed = new PaymentFailed(
            Guid.NewGuid(),
            order.Id,
            "card_declined",
            false,
            DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(paymentFailed);

        // Assert: Verify order transitioned to PaymentFailed
        await using var querySession = _fixture.GetDocumentSession();
        var updatedOrder = await querySession.LoadAsync<Order>(order.Id);

        updatedOrder.ShouldNotBeNull();
        updatedOrder.Status.ShouldBe(OrderStatus.PaymentFailed);
        updatedOrder.CustomerId.ShouldBe(customerId);
        updatedOrder.TotalAmount.ShouldBe(49.99m);
    }

    /// <summary>
    /// Integration test for PaymentAuthorized handler.
    /// Creates an order, sends PaymentAuthorizedIntegration, verifies order transitions to PendingPayment.
    /// **Validates: Requirement 1.4 - Order can wait for deferred payment capture**
    /// </summary>
    [Fact]
    public async Task PaymentAuthorizedIntegration_Transitions_Order_To_PendingPayment()
    {
        // Arrange: Create an order first
        var customerId = Guid.NewGuid();
        var lineItems = new List<CheckoutLineItem>
        {
            new("SKU-003", 3, 29.99m)
        };
        var shippingAddress = new ShippingAddress(
            "789 Elm St",
            "Apt 4B",
            "Seattle",
            "WA",
            "98101",
            "US");

        var checkoutCommand = new CheckoutCompleted(
            Guid.NewGuid(), // CartId
            customerId,
            lineItems,
            shippingAddress,
            "Standard",
            "tok_test_payment",
            null, // AppliedDiscounts
            DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(checkoutCommand);

        // Get the created order
        await using var session = _fixture.GetDocumentSession();
        var order = await session.Query<Order>()
            .Where(o => o.CustomerId == customerId)
            .SingleAsync();

        order.Status.ShouldBe(OrderStatus.Placed);

        // Act: Send PaymentAuthorized message
        var paymentAuthorized = new PaymentAuthorized(
            Guid.NewGuid(),
            order.Id,
            order.TotalAmount,
            "auth_12345",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddDays(7));

        await _fixture.ExecuteAndWaitAsync(paymentAuthorized);

        // Assert: Verify order transitioned to PendingPayment
        await using var querySession = _fixture.GetDocumentSession();
        var updatedOrder = await querySession.LoadAsync<Order>(order.Id);

        updatedOrder.ShouldNotBeNull();
        updatedOrder.Status.ShouldBe(OrderStatus.PendingPayment);
        updatedOrder.CustomerId.ShouldBe(customerId);
        updatedOrder.TotalAmount.ShouldBe(89.97m);
    }

    /// <summary>
    /// Integration test for RefundCompleted handler.
    /// Creates an order, transitions to PaymentConfirmed, then sends RefundCompleted to verify saga receives it.
    /// Refunds don't change order status since they're financial operations after fulfillment.
    /// **Validates: Requirement 1.5 - Order tracks refund completion for financial reconciliation**
    /// </summary>
    [Fact]
    public async Task RefundCompleted_Does_Not_Change_Order_Status()
    {
        // Arrange: Create an order and transition to PaymentConfirmed
        var customerId = Guid.NewGuid();
        var lineItems = new List<CheckoutLineItem>
        {
            new("SKU-004", 1, 99.99m)
        };
        var shippingAddress = new ShippingAddress(
            "321 Pine Ave",
            null,
            "Denver",
            "CO",
            "80201",
            "US");

        var checkoutCommand = new CheckoutCompleted(
            Guid.NewGuid(), // CartId
            customerId,
            lineItems,
            shippingAddress,
            "Express",
            "tok_test_payment",
            null, // AppliedDiscounts
            DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(checkoutCommand);

        // Get the created order
        await using var session = _fixture.GetDocumentSession();
        var order = await session.Query<Order>()
            .Where(o => o.CustomerId == customerId)
            .SingleAsync();

        order.Status.ShouldBe(OrderStatus.Placed);

        // Transition to PaymentConfirmed
        var paymentCaptured = new PaymentCaptured(
            Guid.NewGuid(),
            order.Id,
            order.TotalAmount,
            "txn_67890",
            DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(paymentCaptured);

        // Verify payment confirmed
        await using var confirmedSession = _fixture.GetDocumentSession();
        var confirmedOrder = await confirmedSession.LoadAsync<Order>(order.Id);
        confirmedOrder.Status.ShouldBe(OrderStatus.PaymentConfirmed);

        // Act: Send RefundCompleted message
        var refundCompleted = new RefundCompleted(
            Guid.NewGuid(), // PaymentId
            order.Id,
            49.99m, // Partial refund
            "ref_12345",
            DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(refundCompleted);

        // Assert: Verify order status remains PaymentConfirmed (refunds don't change fulfillment status)
        await using var querySession = _fixture.GetDocumentSession();
        var updatedOrder = await querySession.LoadAsync<Order>(order.Id);

        updatedOrder.ShouldNotBeNull();
        updatedOrder.Status.ShouldBe(OrderStatus.PaymentConfirmed);
        updatedOrder.CustomerId.ShouldBe(customerId);
        updatedOrder.TotalAmount.ShouldBe(99.99m);
    }

    /// <summary>
    /// Integration test for RefundFailed handler.
    /// Creates an order, transitions to PaymentConfirmed, then sends RefundFailed to verify saga receives it.
    /// Refund failures don't change order status since they're financial issues that don't affect fulfillment.
    /// **Validates: Requirement 1.6 - Order tracks refund failures for investigation and retry**
    /// </summary>
    [Fact]
    public async Task RefundFailed_Does_Not_Change_Order_Status()
    {
        // Arrange: Create an order and transition to PaymentConfirmed
        var customerId = Guid.NewGuid();
        var lineItems = new List<CheckoutLineItem>
        {
            new("SKU-005", 2, 24.99m)
        };
        var shippingAddress = new ShippingAddress(
            "555 Maple Dr",
            "Suite 200",
            "Austin",
            "TX",
            "73301",
            "US");

        var checkoutCommand = new CheckoutCompleted(
            Guid.NewGuid(), // CartId
            customerId,
            lineItems,
            shippingAddress,
            "Standard",
            "tok_test_payment",
            null, // AppliedDiscounts
            DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(checkoutCommand);

        // Get the created order
        await using var session = _fixture.GetDocumentSession();
        var order = await session.Query<Order>()
            .Where(o => o.CustomerId == customerId)
            .SingleAsync();

        order.Status.ShouldBe(OrderStatus.Placed);

        // Transition to PaymentConfirmed
        var paymentCaptured = new PaymentCaptured(
            Guid.NewGuid(),
            order.Id,
            order.TotalAmount,
            "txn_99999",
            DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(paymentCaptured);

        // Verify payment confirmed
        await using var confirmedSession = _fixture.GetDocumentSession();
        var confirmedOrder = await confirmedSession.LoadAsync<Order>(order.Id);
        confirmedOrder.Status.ShouldBe(OrderStatus.PaymentConfirmed);

        // Act: Send RefundFailed message
        var refundFailed = new RefundFailed(
            Guid.NewGuid(), // PaymentId
            order.Id,
            "Insufficient funds in merchant account",
            DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(refundFailed);

        // Assert: Verify order status remains PaymentConfirmed (refund failures don't change fulfillment status)
        await using var querySession = _fixture.GetDocumentSession();
        var updatedOrder = await querySession.LoadAsync<Order>(order.Id);

        updatedOrder.ShouldNotBeNull();
        updatedOrder.Status.ShouldBe(OrderStatus.PaymentConfirmed);
        updatedOrder.CustomerId.ShouldBe(customerId);
        updatedOrder.TotalAmount.ShouldBe(49.98m);
    }
}
