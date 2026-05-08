using Messages.Contracts.Payments;
using Orders.Placement;
using IntegrationContracts = Messages.Contracts.Orders;

namespace Orders.UnitTests.Placement;

/// <summary>
/// Unit tests for fraud review / OnHold saga state (M45.1 / S5).
/// Covers <see cref="OrderDecider.CanBePutOnHold"/>, <see cref="OrderDecider.CanBeReleasedFromHold"/>,
/// <see cref="OrderDecider.CanBeRejectedForFraud"/>, and the three decision functions
/// (<c>HandlePutOnHold</c>, <c>HandleReleaseFromHold</c>, <c>HandleRejectForFraud</c>) plus the
/// saga handler integration points.
///
/// Closes the M45.1 / S5 audit finding: <c>OrderStatus.OnHold</c> existed in the enum and was tested
/// as cancellable, but no command transitioned the saga into it ("misleading enum" anti-pattern).
/// </summary>
public sealed class OrderDeciderFraudReviewTests
{
    private const string Reason = "AVS mismatch — manual review required";
    private const string ReviewerId = "reviewer-42";

    private static Order BuildOrder(
        Guid? id = null,
        Guid? customerId = null,
        OrderStatus status = OrderStatus.PaymentConfirmed,
        bool isPaymentCaptured = false,
        Guid? paymentId = null,
        Dictionary<Guid, string>? reservationIds = null,
        decimal totalAmount = 120.00m) =>
        new()
        {
            Id = id ?? Guid.NewGuid(),
            CustomerId = customerId ?? Guid.NewGuid(),
            Status = status,
            IsPaymentCaptured = isPaymentCaptured,
            PaymentId = paymentId,
            ReservationIds = reservationIds ?? new Dictionary<Guid, string>(),
            TotalAmount = totalAmount,
            ShippingAddress = new ShippingAddress("1 Main St", null, "Springfield", "IL", "62701", "US"),
            ShippingMethod = "Standard",
            ExpectedReservationCount = 1,
        };

    // ===========================================================================
    // CanBePutOnHold — eligibility
    // ===========================================================================

    [Theory]
    [InlineData(OrderStatus.Placed)]
    [InlineData(OrderStatus.PendingPayment)]
    [InlineData(OrderStatus.PaymentConfirmed)]
    [InlineData(OrderStatus.InventoryReserved)]
    public void CanBePutOnHold_ReturnsTrue_For_PreFulfillment_Statuses(OrderStatus status)
    {
        OrderDecider.CanBePutOnHold(status).ShouldBeTrue();
    }

    [Theory]
    [InlineData(OrderStatus.OnHold)]                 // already on hold — must release first
    [InlineData(OrderStatus.InventoryCommitted)]
    [InlineData(OrderStatus.Fulfilling)]
    [InlineData(OrderStatus.Shipped)]
    [InlineData(OrderStatus.Delivered)]
    [InlineData(OrderStatus.Closed)]
    [InlineData(OrderStatus.Cancelled)]
    [InlineData(OrderStatus.OutOfStock)]
    [InlineData(OrderStatus.PaymentFailed)]
    public void CanBePutOnHold_ReturnsFalse_For_Ineligible_Statuses(OrderStatus status)
    {
        OrderDecider.CanBePutOnHold(status).ShouldBeFalse();
    }

    // ===========================================================================
    // CanBeReleasedFromHold / CanBeRejectedForFraud
    // ===========================================================================

    [Fact]
    public void CanBeReleasedFromHold_ReturnsTrue_Only_For_OnHold()
    {
        OrderDecider.CanBeReleasedFromHold(OrderStatus.OnHold).ShouldBeTrue();
        OrderDecider.CanBeReleasedFromHold(OrderStatus.PaymentConfirmed).ShouldBeFalse();
        OrderDecider.CanBeReleasedFromHold(OrderStatus.Placed).ShouldBeFalse();
        OrderDecider.CanBeReleasedFromHold(OrderStatus.Cancelled).ShouldBeFalse();
    }

    [Theory]
    [InlineData(OrderStatus.Placed)]
    [InlineData(OrderStatus.PendingPayment)]
    [InlineData(OrderStatus.PaymentConfirmed)]
    [InlineData(OrderStatus.InventoryReserved)]
    [InlineData(OrderStatus.OnHold)]
    public void CanBeRejectedForFraud_ReturnsTrue_For_PreFulfillment_And_OnHold(OrderStatus status)
    {
        OrderDecider.CanBeRejectedForFraud(status).ShouldBeTrue();
    }

    [Theory]
    [InlineData(OrderStatus.InventoryCommitted)]
    [InlineData(OrderStatus.Shipped)]
    [InlineData(OrderStatus.Delivered)]
    [InlineData(OrderStatus.Cancelled)]
    public void CanBeRejectedForFraud_ReturnsFalse_For_PostHandoff_And_Terminal(OrderStatus status)
    {
        OrderDecider.CanBeRejectedForFraud(status).ShouldBeFalse();
    }

    // ===========================================================================
    // HandlePutOnHold
    // ===========================================================================

    [Fact]
    public void HandlePutOnHold_With_Eligible_Status_Transitions_To_OnHold_And_Emits_Event()
    {
        var customerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var order = BuildOrder(id: orderId, customerId: customerId, status: OrderStatus.PaymentConfirmed);

        var decision = OrderDecider.HandlePutOnHold(
            order,
            new PutOrderOnHold(orderId, Reason, ReviewerId),
            DateTimeOffset.UtcNow);

        decision.Status.ShouldBe(OrderStatus.OnHold);
        var msg = decision.Messages.OfType<IntegrationContracts.OrderPutOnHold>().SingleOrDefault();
        msg.ShouldNotBeNull();
        msg.OrderId.ShouldBe(orderId);
        msg.CustomerId.ShouldBe(customerId);
        msg.Reason.ShouldBe(Reason);
        msg.ReviewerId.ShouldBe(ReviewerId);
    }

    [Fact]
    public void HandlePutOnHold_With_Ineligible_Status_Returns_Empty_Decision()
    {
        var order = BuildOrder(status: OrderStatus.Shipped);

        var decision = OrderDecider.HandlePutOnHold(
            order,
            new PutOrderOnHold(order.Id, Reason, ReviewerId),
            DateTimeOffset.UtcNow);

        decision.Status.ShouldBeNull();
        decision.Messages.ShouldBeEmpty();
    }

    [Fact]
    public void HandlePutOnHold_Twice_Is_Idempotent()
    {
        // First put-on-hold succeeds, second is silently ignored.
        var order = BuildOrder(status: OrderStatus.OnHold);

        var decision = OrderDecider.HandlePutOnHold(
            order,
            new PutOrderOnHold(order.Id, Reason, ReviewerId),
            DateTimeOffset.UtcNow);

        decision.Status.ShouldBeNull();
        decision.Messages.ShouldBeEmpty();
    }

    // ===========================================================================
    // HandleReleaseFromHold
    // ===========================================================================

    [Fact]
    public void HandleReleaseFromHold_With_OnHold_Status_Transitions_To_PaymentConfirmed()
    {
        var customerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var order = BuildOrder(id: orderId, customerId: customerId, status: OrderStatus.OnHold);

        var decision = OrderDecider.HandleReleaseFromHold(
            order,
            new ReleaseOrderFromHold(orderId, ReviewerId, "Verified customer identity"),
            DateTimeOffset.UtcNow);

        decision.Status.ShouldBe(OrderStatus.PaymentConfirmed);
        var msg = decision.Messages.OfType<IntegrationContracts.OrderReleasedFromHold>().SingleOrDefault();
        msg.ShouldNotBeNull();
        msg.OrderId.ShouldBe(orderId);
        msg.ReviewerId.ShouldBe(ReviewerId);
        msg.ReleaseNotes.ShouldBe("Verified customer identity");
    }

    [Fact]
    public void HandleReleaseFromHold_When_Not_OnHold_Returns_Empty_Decision()
    {
        var order = BuildOrder(status: OrderStatus.PaymentConfirmed);

        var decision = OrderDecider.HandleReleaseFromHold(
            order,
            new ReleaseOrderFromHold(order.Id, ReviewerId),
            DateTimeOffset.UtcNow);

        decision.Status.ShouldBeNull();
        decision.Messages.ShouldBeEmpty();
    }

    // ===========================================================================
    // HandleRejectForFraud — compensation
    // ===========================================================================

    [Fact]
    public void HandleRejectForFraud_With_Eligible_Status_Transitions_To_Cancelled()
    {
        var order = BuildOrder(status: OrderStatus.OnHold);

        var decision = OrderDecider.HandleRejectForFraud(
            order,
            new RejectOrderForFraud(order.Id, "Stolen card", ReviewerId),
            DateTimeOffset.UtcNow);

        decision.Status.ShouldBe(OrderStatus.Cancelled);
    }

    [Fact]
    public void HandleRejectForFraud_Emits_Domain_Specific_Event_AND_Standard_OrderCancelled()
    {
        // The fraud rejection reuses the cancellation choreography so downstream BCs (Inventory,
        // Fulfillment, Customer Experience) react via the existing path. The domain-specific
        // event is for Backoffice account-flagging and Customer Experience custom messaging.
        var order = BuildOrder(status: OrderStatus.PaymentConfirmed);

        var decision = OrderDecider.HandleRejectForFraud(
            order,
            new RejectOrderForFraud(order.Id, "Stolen card", ReviewerId),
            DateTimeOffset.UtcNow);

        decision.Messages.OfType<IntegrationContracts.OrderRejectedForFraud>().Count().ShouldBe(1);
        decision.Messages.OfType<IntegrationContracts.OrderCancelled>().Count().ShouldBe(1);
    }

    [Fact]
    public void HandleRejectForFraud_Releases_Inventory_For_All_Reservations()
    {
        var reservationIds = new Dictionary<Guid, string>
        {
            { Guid.NewGuid(), "SKU-A" },
            { Guid.NewGuid(), "SKU-B" },
        };
        var order = BuildOrder(
            status: OrderStatus.InventoryReserved,
            reservationIds: reservationIds);

        var decision = OrderDecider.HandleRejectForFraud(
            order,
            new RejectOrderForFraud(order.Id, "Stolen card", ReviewerId),
            DateTimeOffset.UtcNow);

        decision.Messages.OfType<IntegrationContracts.ReservationReleaseRequested>().Count().ShouldBe(2);
    }

    [Fact]
    public void HandleRejectForFraud_Refunds_Captured_Payment()
    {
        var order = BuildOrder(
            status: OrderStatus.PaymentConfirmed,
            isPaymentCaptured: true,
            paymentId: Guid.NewGuid(),
            totalAmount: 99.95m);

        var decision = OrderDecider.HandleRejectForFraud(
            order,
            new RejectOrderForFraud(order.Id, "Stolen card", ReviewerId),
            DateTimeOffset.UtcNow);

        var refund = decision.Messages.OfType<RefundRequested>().SingleOrDefault();
        refund.ShouldNotBeNull();
        refund.Amount.ShouldBe(99.95m);
        refund.OrderId.ShouldBe(order.Id);
    }

    [Fact]
    public void HandleRejectForFraud_When_No_Payment_Captured_Does_Not_Emit_Refund()
    {
        var order = BuildOrder(status: OrderStatus.Placed, isPaymentCaptured: false);

        var decision = OrderDecider.HandleRejectForFraud(
            order,
            new RejectOrderForFraud(order.Id, "Stolen card", ReviewerId),
            DateTimeOffset.UtcNow);

        decision.Messages.OfType<RefundRequested>().ShouldBeEmpty();
    }

    [Fact]
    public void HandleRejectForFraud_With_Ineligible_Status_Returns_Empty_Decision()
    {
        var order = BuildOrder(status: OrderStatus.Shipped);

        var decision = OrderDecider.HandleRejectForFraud(
            order,
            new RejectOrderForFraud(order.Id, "Stolen card", ReviewerId),
            DateTimeOffset.UtcNow);

        decision.Status.ShouldBeNull();
        decision.Messages.ShouldBeEmpty();
    }

    // ===========================================================================
    // Saga handler integration
    // ===========================================================================

    [Fact]
    public void Saga_Handle_PutOnHold_Mutates_Status()
    {
        var order = BuildOrder(status: OrderStatus.PaymentConfirmed);

        var outgoing = order.Handle(new PutOrderOnHold(order.Id, Reason, ReviewerId));

        order.Status.ShouldBe(OrderStatus.OnHold);
        outgoing.OfType<IntegrationContracts.OrderPutOnHold>().Count().ShouldBe(1);
    }

    [Fact]
    public void Saga_Handle_ReleaseFromHold_Returns_To_PaymentConfirmed()
    {
        var order = BuildOrder(status: OrderStatus.OnHold);

        var outgoing = order.Handle(new ReleaseOrderFromHold(order.Id, ReviewerId));

        order.Status.ShouldBe(OrderStatus.PaymentConfirmed);
        outgoing.OfType<IntegrationContracts.OrderReleasedFromHold>().Count().ShouldBe(1);
    }

    [Fact]
    public void Saga_Handle_RejectForFraud_With_No_Payment_Closes_Saga()
    {
        // Mirrors Handle(CancelOrder) behavior: when no payment was captured, there is no
        // RefundCompleted to await, so the saga closes immediately.
        var order = BuildOrder(status: OrderStatus.Placed, isPaymentCaptured: false);

        order.Handle(new RejectOrderForFraud(order.Id, "Stolen card", ReviewerId));

        order.Status.ShouldBe(OrderStatus.Cancelled);
        order.IsCompleted().ShouldBeTrue();
    }

    [Fact]
    public void Saga_Handle_RejectForFraud_With_Captured_Payment_Stays_Open_For_Refund()
    {
        // When payment was captured, the saga must stay open to await RefundCompleted from Payments.
        var order = BuildOrder(
            status: OrderStatus.PaymentConfirmed,
            isPaymentCaptured: true,
            paymentId: Guid.NewGuid());

        order.Handle(new RejectOrderForFraud(order.Id, "Stolen card", ReviewerId));

        order.Status.ShouldBe(OrderStatus.Cancelled);
        order.IsCompleted().ShouldBeFalse();
    }
}
