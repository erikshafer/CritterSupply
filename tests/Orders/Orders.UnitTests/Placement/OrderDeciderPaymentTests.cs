using Messages.Contracts.Payments;
using Orders.Placement;
using IntegrationContracts = Messages.Contracts.Orders;

namespace Orders.UnitTests.Placement;

/// <summary>
/// Unit tests for all payment-related <see cref="OrderDecider"/> methods:
/// <list type="bullet">
///   <item><see cref="OrderDecider.HandlePaymentCaptured"/></item>
///   <item><see cref="OrderDecider.HandlePaymentFailed"/></item>
///   <item><see cref="OrderDecider.HandlePaymentAuthorized"/></item>
///   <item><see cref="OrderDecider.HandleRefundCompleted"/></item>
///   <item><see cref="OrderDecider.HandleRefundFailed"/></item>
/// </list>
/// </summary>
public class OrderDeciderPaymentTests
{
    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static Order BuildOrder(
        Guid? id = null,
        OrderStatus status = OrderStatus.Placed,
        bool isPaymentCaptured = false,
        bool isInventoryReserved = false,
        Dictionary<Guid, string>? reservationIds = null,
        int expectedReservationCount = 1,
        decimal totalAmount = 100.00m)
    {
        // Derive reservation state from provided reservationIds when isInventoryReserved=true
        var resIds = reservationIds ?? new Dictionary<Guid, string>();
        var confirmedCount = isInventoryReserved ? Math.Max(resIds.Count, expectedReservationCount) : 0;

        return new Order
        {
            Id = id ?? Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Status = status,
            IsPaymentCaptured = isPaymentCaptured,
            ReservationIds = resIds,
            ExpectedReservationCount = expectedReservationCount,
            ConfirmedReservationCount = confirmedCount,
            TotalAmount = totalAmount,
            ShippingAddress = new ShippingAddress("1 Main St", null, "City", "ST", "00001", "US"),
            ShippingMethod = "Standard",
        };
    }

    private static PaymentCaptured BuildPaymentCaptured(Guid? orderId = null) =>
        new(Guid.NewGuid(), orderId ?? Guid.NewGuid(), 100.00m, "txn_abc", DateTimeOffset.UtcNow);

    private static PaymentFailed BuildPaymentFailed(Guid? orderId = null) =>
        new(Guid.NewGuid(), orderId ?? Guid.NewGuid(), "Card declined", false, DateTimeOffset.UtcNow);

    private static PaymentAuthorized BuildPaymentAuthorized(Guid? orderId = null) =>
        new(Guid.NewGuid(), orderId ?? Guid.NewGuid(), 100.00m, "auth_xyz",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1));

    private static RefundCompleted BuildRefundCompleted(Guid? orderId = null) =>
        new(Guid.NewGuid(), orderId ?? Guid.NewGuid(), 100.00m, "txn_refund", DateTimeOffset.UtcNow);

    private static RefundFailed BuildRefundFailed(Guid? orderId = null) =>
        new(Guid.NewGuid(), orderId ?? Guid.NewGuid(), "Insufficient balance", DateTimeOffset.UtcNow);

    // ===========================================================================
    // HandlePaymentCaptured
    // ===========================================================================

    /// <summary>HandlePaymentCaptured must set Status to PaymentConfirmed.</summary>
    [Fact]
    public void HandlePaymentCaptured_SetsStatus_ToPaymentConfirmed()
    {
        var order = BuildOrder(status: OrderStatus.Placed);

        var decision = OrderDecider.HandlePaymentCaptured(order, BuildPaymentCaptured(), DateTimeOffset.UtcNow);

        decision.Status.ShouldBe(OrderStatus.PaymentConfirmed);
    }

    /// <summary>HandlePaymentCaptured must set IsPaymentCaptured to true.</summary>
    [Fact]
    public void HandlePaymentCaptured_SetsIsPaymentCaptured_ToTrue()
    {
        var order = BuildOrder(isPaymentCaptured: false);

        var decision = OrderDecider.HandlePaymentCaptured(order, BuildPaymentCaptured(), DateTimeOffset.UtcNow);

        decision.IsPaymentCaptured.ShouldBe(true);
    }

    /// <summary>
    /// When inventory IS already fully reserved, HandlePaymentCaptured must emit
    /// one ReservationCommitRequested per reservation.
    /// </summary>
    [Fact]
    public void HandlePaymentCaptured_WhenInventoryReserved_EmitsCommitRequestPerReservation()
    {
        var res1 = Guid.NewGuid();
        var res2 = Guid.NewGuid();
        var order = BuildOrder(
            isInventoryReserved: true,
            expectedReservationCount: 2,
            reservationIds: new Dictionary<Guid, string> { [res1] = "SKU-A", [res2] = "SKU-B" });
        var timestamp = DateTimeOffset.UtcNow;

        var decision = OrderDecider.HandlePaymentCaptured(order, BuildPaymentCaptured(), timestamp);

        var commits = decision.Messages
            .OfType<IntegrationContracts.ReservationCommitRequested>()
            .ToList();

        commits.Count.ShouldBe(2);
        commits.ShouldContain(c => c.ReservationId == res1 && c.RequestedAt == timestamp);
        commits.ShouldContain(c => c.ReservationId == res2 && c.RequestedAt == timestamp);
    }

    /// <summary>
    /// When inventory is NOT yet fully reserved, HandlePaymentCaptured must emit NO messages
    /// (it waits for ReservationConfirmed to arrive and then commits).
    /// </summary>
    [Fact]
    public void HandlePaymentCaptured_WhenInventoryNotReserved_EmitsNoMessages()
    {
        var order = BuildOrder(isInventoryReserved: false, expectedReservationCount: 2);

        var decision = OrderDecider.HandlePaymentCaptured(order, BuildPaymentCaptured(), DateTimeOffset.UtcNow);

        decision.Messages.ShouldBeEmpty();
    }

    /// <summary>
    /// ReservationCommitRequested messages must carry the correct OrderId.
    /// </summary>
    [Fact]
    public void HandlePaymentCaptured_CommitRequests_HaveCorrectOrderId()
    {
        var orderId = Guid.NewGuid();
        var resId = Guid.NewGuid();
        var order = BuildOrder(
            id: orderId,
            isInventoryReserved: true,
            expectedReservationCount: 1,
            reservationIds: new Dictionary<Guid, string> { [resId] = "SKU-X" });

        var decision = OrderDecider.HandlePaymentCaptured(order, BuildPaymentCaptured(orderId), DateTimeOffset.UtcNow);

        var commit = decision.Messages.OfType<IntegrationContracts.ReservationCommitRequested>().Single();
        commit.OrderId.ShouldBe(orderId);
    }

    // ===========================================================================
    // HandlePaymentFailed
    // ===========================================================================

    /// <summary>HandlePaymentFailed must set Status to PaymentFailed.</summary>
    [Fact]
    public void HandlePaymentFailed_SetsStatus_ToPaymentFailed()
    {
        var order = BuildOrder();

        var decision = OrderDecider.HandlePaymentFailed(order, BuildPaymentFailed(), DateTimeOffset.UtcNow);

        decision.Status.ShouldBe(OrderStatus.PaymentFailed);
    }

    /// <summary>
    /// When inventory IS reserved, HandlePaymentFailed must emit a ReservationReleaseRequested
    /// for each reservation (compensation flow).
    /// </summary>
    [Fact]
    public void HandlePaymentFailed_WhenInventoryReserved_EmitsReleasePerReservation()
    {
        var res1 = Guid.NewGuid();
        var res2 = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var order = BuildOrder(
            id: orderId,
            isInventoryReserved: true,
            expectedReservationCount: 2,
            reservationIds: new Dictionary<Guid, string> { [res1] = "SKU-A", [res2] = "SKU-B" });
        var timestamp = DateTimeOffset.UtcNow;

        var decision = OrderDecider.HandlePaymentFailed(order, BuildPaymentFailed(orderId), timestamp);

        var releases = decision.Messages
            .OfType<IntegrationContracts.ReservationReleaseRequested>()
            .ToList();

        releases.Count.ShouldBe(2);
        releases.ShouldContain(r => r.ReservationId == res1 && r.OrderId == orderId);
        releases.ShouldContain(r => r.ReservationId == res2 && r.OrderId == orderId);
    }

    /// <summary>
    /// Release messages for payment failure must carry the "Payment failed" reason.
    /// </summary>
    [Fact]
    public void HandlePaymentFailed_ReleaseRequests_HavePaymentFailedReason()
    {
        var res = Guid.NewGuid();
        var order = BuildOrder(
            isInventoryReserved: true,
            expectedReservationCount: 1,
            reservationIds: new Dictionary<Guid, string> { [res] = "SKU-X" });

        var decision = OrderDecider.HandlePaymentFailed(order, BuildPaymentFailed(), DateTimeOffset.UtcNow);

        var release = decision.Messages.OfType<IntegrationContracts.ReservationReleaseRequested>().Single();
        release.Reason.ShouldBe("Payment failed");
    }

    /// <summary>
    /// When inventory is NOT reserved, HandlePaymentFailed must emit no compensation messages.
    /// </summary>
    [Fact]
    public void HandlePaymentFailed_WhenInventoryNotReserved_EmitsNoMessages()
    {
        var order = BuildOrder(isInventoryReserved: false, expectedReservationCount: 1);

        var decision = OrderDecider.HandlePaymentFailed(order, BuildPaymentFailed(), DateTimeOffset.UtcNow);

        decision.Messages.ShouldBeEmpty();
    }

    // ===========================================================================
    // HandlePaymentAuthorized
    // ===========================================================================

    /// <summary>HandlePaymentAuthorized must set Status to PendingPayment.</summary>
    [Fact]
    public void HandlePaymentAuthorized_SetsStatus_ToPendingPayment()
    {
        var order = BuildOrder(status: OrderStatus.Placed);

        var decision = OrderDecider.HandlePaymentAuthorized(order, BuildPaymentAuthorized());

        decision.Status.ShouldBe(OrderStatus.PendingPayment);
    }

    /// <summary>HandlePaymentAuthorized must emit no outgoing messages (pure status transition).</summary>
    [Fact]
    public void HandlePaymentAuthorized_EmitsNoMessages()
    {
        var order = BuildOrder();

        var decision = OrderDecider.HandlePaymentAuthorized(order, BuildPaymentAuthorized());

        decision.Messages.ShouldBeEmpty();
    }

    // ===========================================================================
    // HandleRefundCompleted
    // ===========================================================================

    /// <summary>
    /// When order Status is Cancelled, a completed refund must set Status to Closed
    /// and signal saga completion.
    /// </summary>
    [Fact]
    public void HandleRefundCompleted_WhenStatusCancelled_SetsStatusToClosedAndShouldComplete()
    {
        var order = BuildOrder(status: OrderStatus.Cancelled);

        var decision = OrderDecider.HandleRefundCompleted(order, BuildRefundCompleted());

        decision.Status.ShouldBe(OrderStatus.Closed);
        decision.ShouldComplete.ShouldBeTrue();
    }

    /// <summary>
    /// When order Status is OutOfStock, a completed refund must set Status to Closed
    /// and signal saga completion (financial lifecycle closed).
    /// </summary>
    [Fact]
    public void HandleRefundCompleted_WhenStatusOutOfStock_SetsStatusToClosedAndShouldComplete()
    {
        var order = BuildOrder(status: OrderStatus.OutOfStock);

        var decision = OrderDecider.HandleRefundCompleted(order, BuildRefundCompleted());

        decision.Status.ShouldBe(OrderStatus.Closed);
        decision.ShouldComplete.ShouldBeTrue();
    }

    /// <summary>
    /// When order Status is anything other than Cancelled or OutOfStock,
    /// HandleRefundCompleted must produce no state changes.
    /// </summary>
    [Theory]
    [InlineData(OrderStatus.Placed)]
    [InlineData(OrderStatus.PendingPayment)]
    [InlineData(OrderStatus.PaymentConfirmed)]
    [InlineData(OrderStatus.InventoryReserved)]
    [InlineData(OrderStatus.Fulfilling)]
    [InlineData(OrderStatus.Shipped)]
    [InlineData(OrderStatus.Delivered)]
    public void HandleRefundCompleted_WhenStatusIsOther_ReturnsNoOp(OrderStatus status)
    {
        var order = BuildOrder(status: status);

        var decision = OrderDecider.HandleRefundCompleted(order, BuildRefundCompleted());

        decision.Status.ShouldBeNull();
        decision.ShouldComplete.ShouldBeFalse();
        decision.Messages.ShouldBeEmpty();
    }

    // ===========================================================================
    // HandleRefundFailed
    // ===========================================================================

    /// <summary>HandleRefundFailed must not change any status (failure tracked by Payments BC).</summary>
    [Fact]
    public void HandleRefundFailed_DoesNotChangeStatus()
    {
        var order = BuildOrder(status: OrderStatus.Cancelled);

        var decision = OrderDecider.HandleRefundFailed(order, BuildRefundFailed());

        decision.Status.ShouldBeNull();
    }

    /// <summary>HandleRefundFailed must emit no messages (retry is the Payments BC's responsibility).</summary>
    [Fact]
    public void HandleRefundFailed_EmitsNoMessages()
    {
        var order = BuildOrder();

        var decision = OrderDecider.HandleRefundFailed(order, BuildRefundFailed());

        decision.Messages.ShouldBeEmpty();
    }

    /// <summary>HandleRefundFailed must not signal saga completion.</summary>
    [Fact]
    public void HandleRefundFailed_DoesNotSignalCompletion()
    {
        var order = BuildOrder(status: OrderStatus.Cancelled);

        var decision = OrderDecider.HandleRefundFailed(order, BuildRefundFailed());

        decision.ShouldComplete.ShouldBeFalse();
    }
}
