using Orders.Placement;
using IntegrationContracts = Messages.Contracts.Orders;
using PaymentContracts = Messages.Contracts.Payments;

namespace Orders.UnitTests.Placement;

/// <summary>
/// Unit tests for <see cref="OrderDecider.CanBeCancelled"/> and
/// <see cref="OrderDecider.HandleCancelOrder"/>.
/// Verifies cancellation guard logic and compensation message generation.
/// </summary>
public class OrderDeciderCancellationTests
{
    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static Order BuildOrder(
        Guid? id = null,
        Guid? customerId = null,
        OrderStatus status = OrderStatus.Placed,
        bool isPaymentCaptured = false,
        Dictionary<Guid, string>? reservationIds = null,
        decimal totalAmount = 100.00m)
    {
        return new Order
        {
            Id = id ?? Guid.NewGuid(),
            CustomerId = customerId ?? Guid.NewGuid(),
            Status = status,
            IsPaymentCaptured = isPaymentCaptured,
            ReservationIds = reservationIds ?? new Dictionary<Guid, string>(),
            TotalAmount = totalAmount,
            ShippingAddress = new ShippingAddress("1 Main St", null, "Springfield", "IL", "62701", "US"),
            ShippingMethod = "Standard",
            ExpectedReservationCount = 1,
        };
    }

    private static CancelOrder BuildCancelCommand(Guid? orderId = null, string reason = "Customer request") =>
        new(orderId ?? Guid.NewGuid(), reason);

    // ---------------------------------------------------------------------------
    // CanBeCancelled — statuses that ALLOW cancellation
    // ---------------------------------------------------------------------------

    [Theory]
    [InlineData(OrderStatus.Placed)]
    [InlineData(OrderStatus.PendingPayment)]
    [InlineData(OrderStatus.PaymentConfirmed)]
    [InlineData(OrderStatus.InventoryReserved)]
    [InlineData(OrderStatus.InventoryCommitted)]
    [InlineData(OrderStatus.OnHold)]
    [InlineData(OrderStatus.Fulfilling)]
    [InlineData(OrderStatus.Shipped)]
    [InlineData(OrderStatus.DeliveryFailed)]
    [InlineData(OrderStatus.Reshipping)]
    [InlineData(OrderStatus.Backordered)]
    public void CanBeCancelled_ReturnsTrue_ForCancellableStatuses(OrderStatus status)
    {
        OrderDecider.CanBeCancelled(status).ShouldBeTrue();
    }

    // ---------------------------------------------------------------------------
    // CanBeCancelled — statuses that BLOCK cancellation
    // ---------------------------------------------------------------------------

    [Theory]
    [InlineData(OrderStatus.Delivered)]
    [InlineData(OrderStatus.Closed)]
    [InlineData(OrderStatus.Cancelled)]
    [InlineData(OrderStatus.OutOfStock)]
    [InlineData(OrderStatus.PaymentFailed)]
    public void CanBeCancelled_ReturnsFalse_ForNonCancellableStatuses(OrderStatus status)
    {
        OrderDecider.CanBeCancelled(status).ShouldBeFalse();
    }

    // ---------------------------------------------------------------------------
    // HandleCancelOrder — status transition
    // ---------------------------------------------------------------------------

    /// <summary>HandleCancelOrder must always set Status to Cancelled regardless of starting state.</summary>
    [Fact]
    public void HandleCancelOrder_AlwaysSetsStatus_ToCancelled()
    {
        var order = BuildOrder(status: OrderStatus.Placed);
        var command = BuildCancelCommand(orderId: order.Id);
        var timestamp = DateTimeOffset.UtcNow;

        var decision = OrderDecider.HandleCancelOrder(order, command, timestamp);

        decision.Status.ShouldBe(OrderStatus.Cancelled);
    }

    // ---------------------------------------------------------------------------
    // HandleCancelOrder — OrderCancelled integration event always emitted
    // ---------------------------------------------------------------------------

    /// <summary>OrderCancelled integration event must always be the last message emitted.</summary>
    [Fact]
    public void HandleCancelOrder_Always_EmitsOrderCancelledIntegrationEvent()
    {
        var order = BuildOrder();
        var command = BuildCancelCommand(orderId: order.Id, reason: "Changed mind");
        var timestamp = DateTimeOffset.UtcNow;

        var decision = OrderDecider.HandleCancelOrder(order, command, timestamp);

        var orderCancelled = decision.Messages
            .OfType<IntegrationContracts.OrderCancelled>()
            .SingleOrDefault();

        orderCancelled.ShouldNotBeNull();
        orderCancelled.OrderId.ShouldBe(order.Id);
        orderCancelled.CustomerId.ShouldBe(order.CustomerId);
        orderCancelled.Reason.ShouldBe("Changed mind");
        orderCancelled.CancelledAt.ShouldBe(timestamp);
    }

    // ---------------------------------------------------------------------------
    // HandleCancelOrder — no reservations, no payment captured
    // ---------------------------------------------------------------------------

    /// <summary>
    /// When there are no reservations and payment is not captured,
    /// the only message emitted is OrderCancelled.
    /// </summary>
    [Fact]
    public void HandleCancelOrder_NoReservations_NoPaymentCaptured_EmitsOnlyOrderCancelled()
    {
        var order = BuildOrder(isPaymentCaptured: false, reservationIds: new Dictionary<Guid, string>());
        var command = BuildCancelCommand(orderId: order.Id);

        var decision = OrderDecider.HandleCancelOrder(order, command, DateTimeOffset.UtcNow);

        decision.Messages.Count.ShouldBe(1);
        decision.Messages.Single().ShouldBeOfType<IntegrationContracts.OrderCancelled>();
    }

    // ---------------------------------------------------------------------------
    // HandleCancelOrder — inventory compensation
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Each reservation ID must produce exactly one ReservationReleaseRequested message.
    /// </summary>
    [Fact]
    public void HandleCancelOrder_WithReservations_EmitsReleasePerReservation()
    {
        var res1 = Guid.NewGuid();
        var res2 = Guid.NewGuid();
        var order = BuildOrder(
            reservationIds: new Dictionary<Guid, string>
            {
                [res1] = "SKU-001",
                [res2] = "SKU-002",
            });
        var command = BuildCancelCommand(orderId: order.Id, reason: "Out of stock");
        var timestamp = DateTimeOffset.UtcNow;

        var decision = OrderDecider.HandleCancelOrder(order, command, timestamp);

        var releases = decision.Messages
            .OfType<IntegrationContracts.ReservationReleaseRequested>()
            .ToList();

        releases.Count.ShouldBe(2);
        releases.ShouldContain(r => r.ReservationId == res1 && r.OrderId == order.Id && r.Reason == "Out of stock");
        releases.ShouldContain(r => r.ReservationId == res2 && r.OrderId == order.Id && r.Reason == "Out of stock");
    }

    /// <summary>
    /// With two reservations and no payment captured,
    /// messages are: ReleaseRequested × 2 + OrderCancelled = 3 total.
    /// </summary>
    [Fact]
    public void HandleCancelOrder_WithReservations_NoPaymentCaptured_EmitsReleasesAndCancelled()
    {
        var order = BuildOrder(
            isPaymentCaptured: false,
            reservationIds: new Dictionary<Guid, string>
            {
                [Guid.NewGuid()] = "SKU-A",
                [Guid.NewGuid()] = "SKU-B",
            });

        var decision = OrderDecider.HandleCancelOrder(order, BuildCancelCommand(), DateTimeOffset.UtcNow);

        decision.Messages.Count.ShouldBe(3);
        decision.Messages.OfType<IntegrationContracts.ReservationReleaseRequested>().Count().ShouldBe(2);
        decision.Messages.OfType<IntegrationContracts.OrderCancelled>().Count().ShouldBe(1);
    }

    // ---------------------------------------------------------------------------
    // HandleCancelOrder — payment compensation
    // ---------------------------------------------------------------------------

    /// <summary>
    /// When payment IS captured, RefundRequested must be emitted with the order's total amount.
    /// </summary>
    [Fact]
    public void HandleCancelOrder_WhenPaymentCaptured_EmitsRefundRequested()
    {
        var order = BuildOrder(isPaymentCaptured: true, totalAmount: 149.99m);
        var command = BuildCancelCommand(orderId: order.Id, reason: "Fraud detected");
        var timestamp = DateTimeOffset.UtcNow;

        var decision = OrderDecider.HandleCancelOrder(order, command, timestamp);

        var refund = decision.Messages
            .OfType<PaymentContracts.RefundRequested>()
            .SingleOrDefault();

        refund.ShouldNotBeNull();
        refund.OrderId.ShouldBe(order.Id);
        refund.Amount.ShouldBe(149.99m);
        refund.Reason.ShouldBe("Fraud detected");
        refund.RequestedAt.ShouldBe(timestamp);
    }

    /// <summary>
    /// When payment is NOT captured, RefundRequested must NOT be emitted.
    /// </summary>
    [Fact]
    public void HandleCancelOrder_WhenPaymentNotCaptured_DoesNotEmitRefundRequested()
    {
        var order = BuildOrder(isPaymentCaptured: false);
        var decision = OrderDecider.HandleCancelOrder(order, BuildCancelCommand(), DateTimeOffset.UtcNow);

        decision.Messages.OfType<PaymentContracts.RefundRequested>().ShouldBeEmpty();
    }

    /// <summary>
    /// Full cancellation scenario: reservations + payment captured.
    /// Messages: ReleaseRequested × N + RefundRequested + OrderCancelled.
    /// </summary>
    [Fact]
    public void HandleCancelOrder_WithReservationsAndPaymentCaptured_EmitsFullCompensation()
    {
        var res1 = Guid.NewGuid();
        var order = BuildOrder(
            isPaymentCaptured: true,
            totalAmount: 75.00m,
            reservationIds: new Dictionary<Guid, string> { [res1] = "SKU-X" });

        var decision = OrderDecider.HandleCancelOrder(order, BuildCancelCommand(), DateTimeOffset.UtcNow);

        decision.Messages.OfType<IntegrationContracts.ReservationReleaseRequested>().Count().ShouldBe(1);
        decision.Messages.OfType<PaymentContracts.RefundRequested>().Count().ShouldBe(1);
        decision.Messages.OfType<IntegrationContracts.OrderCancelled>().Count().ShouldBe(1);
        decision.Messages.Count.ShouldBe(3);
    }

    /// <summary>
    /// ReservationReleaseRequested messages must come before RefundRequested
    /// which must come before OrderCancelled (message ordering convention).
    /// </summary>
    [Fact]
    public void HandleCancelOrder_MessageOrdering_ReleaseThenRefundThenCancelled()
    {
        var order = BuildOrder(
            isPaymentCaptured: true,
            reservationIds: new Dictionary<Guid, string> { [Guid.NewGuid()] = "SKU-Z" });

        var decision = OrderDecider.HandleCancelOrder(order, BuildCancelCommand(), DateTimeOffset.UtcNow);

        var msgs = decision.Messages.ToList();
        msgs[0].ShouldBeOfType<IntegrationContracts.ReservationReleaseRequested>();
        msgs[1].ShouldBeOfType<PaymentContracts.RefundRequested>();
        msgs[2].ShouldBeOfType<IntegrationContracts.OrderCancelled>();
    }
}
