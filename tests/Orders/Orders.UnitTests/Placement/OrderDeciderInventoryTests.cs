using Messages.Contracts.Inventory;
using Messages.Contracts.Payments;
using Orders.Placement;
using IntegrationContracts = Messages.Contracts.Orders;

namespace Orders.UnitTests.Placement;

/// <summary>
/// Unit tests for inventory reservation-related <see cref="OrderDecider"/> methods:
/// <list type="bullet">
///   <item><see cref="OrderDecider.HandleReservationConfirmed"/></item>
///   <item><see cref="OrderDecider.HandleReservationFailed"/></item>
///   <item><see cref="OrderDecider.HandleReservationCommitted"/></item>
///   <item><see cref="OrderDecider.HandleReservationReleased"/></item>
/// </list>
/// </summary>
public class OrderDeciderInventoryTests
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
        HashSet<Guid>? committedReservationIds = null,
        int confirmedReservationCount = 0,
        int expectedReservationCount = 1,
        decimal totalAmount = 50.00m,
        IReadOnlyList<OrderLineItem>? lineItems = null)
    {
        return new Order
        {
            Id = id ?? Guid.NewGuid(),
            CustomerId = customerId ?? Guid.NewGuid(),
            Status = status,
            IsPaymentCaptured = isPaymentCaptured,
            ReservationIds = reservationIds ?? new Dictionary<Guid, string>(),
            CommittedReservationIds = committedReservationIds ?? [],
            ConfirmedReservationCount = confirmedReservationCount,
            ExpectedReservationCount = expectedReservationCount,
            TotalAmount = totalAmount,
            ShippingAddress = new ShippingAddress("1 Test Ln", null, "Portland", "OR", "97201", "US"),
            ShippingMethod = "Ground",
            LineItems = lineItems ?? [new OrderLineItem("SKU-001", 1, 50.00m, 50.00m)],
        };
    }

    private static ReservationConfirmed BuildReservationConfirmed(
        Guid? orderId = null,
        Guid? reservationId = null,
        string sku = "SKU-001") =>
        new(orderId ?? Guid.NewGuid(),
            Guid.NewGuid(),
            reservationId ?? Guid.NewGuid(),
            sku,
            "WH-01",
            1,
            DateTimeOffset.UtcNow);

    private static ReservationFailed BuildReservationFailed(
        Guid? orderId = null,
        string sku = "SKU-001",
        string reason = "Insufficient stock") =>
        new(orderId ?? Guid.NewGuid(),
            Guid.NewGuid(),
            sku,
            "WH-01",
            1,
            0,
            reason,
            DateTimeOffset.UtcNow);

    private static ReservationCommitted BuildReservationCommitted(
        Guid? orderId = null,
        Guid? reservationId = null,
        string sku = "SKU-001") =>
        new(orderId ?? Guid.NewGuid(),
            Guid.NewGuid(),
            reservationId ?? Guid.NewGuid(),
            sku,
            "WH-01",
            1,
            DateTimeOffset.UtcNow);

    private static ReservationReleased BuildReservationReleased(
        Guid? orderId = null,
        Guid? reservationId = null) =>
        new(orderId ?? Guid.NewGuid(),
            Guid.NewGuid(),
            reservationId ?? Guid.NewGuid(),
            "SKU-001",
            "WH-01",
            1,
            "Order cancelled",
            DateTimeOffset.UtcNow);

    // ===========================================================================
    // HandleReservationConfirmed — terminal state guard
    // ===========================================================================

    /// <summary>
    /// A ReservationConfirmed arriving for a Cancelled order must be silently ignored
    /// to prevent spurious commit requests on a dead order.
    /// </summary>
    [Theory]
    [InlineData(OrderStatus.Cancelled)]
    [InlineData(OrderStatus.OutOfStock)]
    [InlineData(OrderStatus.PaymentFailed)]
    [InlineData(OrderStatus.Closed)]
    public void HandleReservationConfirmed_InTerminalState_ReturnsNoOp(OrderStatus status)
    {
        var order = BuildOrder(status: status);
        var message = BuildReservationConfirmed(orderId: order.Id);

        var decision = OrderDecider.HandleReservationConfirmed(order, message, DateTimeOffset.UtcNow);

        decision.Status.ShouldBeNull();
        decision.Messages.ShouldBeEmpty();
        decision.ReservationIds.ShouldBeNull();
        decision.ConfirmedReservationCount.ShouldBeNull();
    }

    // ===========================================================================
    // HandleReservationConfirmed — idempotency guard
    // ===========================================================================

    /// <summary>
    /// A duplicate ReservationConfirmed (same ReservationId already in ReservationIds)
    /// must return a no-op to prevent double-counting.
    /// </summary>
    [Fact]
    public void HandleReservationConfirmed_DuplicateReservation_ReturnsNoOp()
    {
        var existingRes = Guid.NewGuid();
        var order = BuildOrder(
            reservationIds: new Dictionary<Guid, string> { [existingRes] = "SKU-001" },
            confirmedReservationCount: 1,
            expectedReservationCount: 2);
        var duplicate = BuildReservationConfirmed(orderId: order.Id, reservationId: existingRes);

        var decision = OrderDecider.HandleReservationConfirmed(order, duplicate, DateTimeOffset.UtcNow);

        decision.Status.ShouldBeNull();
        decision.Messages.ShouldBeEmpty();
        decision.ConfirmedReservationCount.ShouldBeNull();
    }

    // ===========================================================================
    // HandleReservationConfirmed — reservation tracking
    // ===========================================================================

    /// <summary>
    /// A new ReservationConfirmed must add the ReservationId to ReservationIds
    /// and increment ConfirmedReservationCount.
    /// </summary>
    [Fact]
    public void HandleReservationConfirmed_NewReservation_AddsToReservationIdsAndIncrementsCount()
    {
        var order = BuildOrder(expectedReservationCount: 2, confirmedReservationCount: 0);
        var newResId = Guid.NewGuid();
        var message = BuildReservationConfirmed(orderId: order.Id, reservationId: newResId, sku: "SKU-001");

        var decision = OrderDecider.HandleReservationConfirmed(order, message, DateTimeOffset.UtcNow);

        decision.ReservationIds.ShouldNotBeNull();
        decision.ReservationIds!.ShouldContainKey(newResId);
        decision.ConfirmedReservationCount.ShouldBe(1);
    }

    /// <summary>
    /// Status must remain unchanged when not all reservations have been confirmed yet.
    /// </summary>
    [Fact]
    public void HandleReservationConfirmed_NotAllReserved_StatusRemainsUnchanged()
    {
        var order = BuildOrder(status: OrderStatus.Placed, expectedReservationCount: 3, confirmedReservationCount: 0);
        var message = BuildReservationConfirmed(orderId: order.Id);

        var decision = OrderDecider.HandleReservationConfirmed(order, message, DateTimeOffset.UtcNow);

        // Only 1 of 3 confirmed — not yet InventoryReserved
        decision.Status.ShouldBe(OrderStatus.Placed);
    }

    /// <summary>
    /// Status must change to InventoryReserved when ALL expected reservations are confirmed.
    /// </summary>
    [Fact]
    public void HandleReservationConfirmed_AllReserved_SetsStatusToInventoryReserved()
    {
        // 1 of 2 already confirmed; this is the second (final) confirmation
        var firstRes = Guid.NewGuid();
        var order = BuildOrder(
            status: OrderStatus.Placed,
            expectedReservationCount: 2,
            confirmedReservationCount: 1,
            reservationIds: new Dictionary<Guid, string> { [firstRes] = "SKU-A" });
        var secondResId = Guid.NewGuid();
        var message = BuildReservationConfirmed(orderId: order.Id, reservationId: secondResId, sku: "SKU-B");

        var decision = OrderDecider.HandleReservationConfirmed(order, message, DateTimeOffset.UtcNow);

        decision.Status.ShouldBe(OrderStatus.InventoryReserved);
    }

    // ===========================================================================
    // HandleReservationConfirmed — payment orchestration
    // ===========================================================================

    /// <summary>
    /// When payment is NOT yet captured, HandleReservationConfirmed must emit no messages.
    /// </summary>
    [Fact]
    public void HandleReservationConfirmed_WhenPaymentNotCaptured_EmitsNoMessages()
    {
        var order = BuildOrder(isPaymentCaptured: false, expectedReservationCount: 1);
        var message = BuildReservationConfirmed(orderId: order.Id);

        var decision = OrderDecider.HandleReservationConfirmed(order, message, DateTimeOffset.UtcNow);

        decision.Messages.ShouldBeEmpty();
    }

    /// <summary>
    /// When payment IS already captured, HandleReservationConfirmed must emit
    /// ReservationCommitRequested for ALL confirmed reservations (including the new one).
    /// This handles the race where payment arrived between individual confirmation messages.
    /// </summary>
    [Fact]
    public void HandleReservationConfirmed_WhenPaymentAlreadyCaptured_EmitsCommitForAllReservations()
    {
        var existingRes = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var order = BuildOrder(
            id: orderId,
            isPaymentCaptured: true,
            expectedReservationCount: 2,
            confirmedReservationCount: 1,
            reservationIds: new Dictionary<Guid, string> { [existingRes] = "SKU-A" });
        var newResId = Guid.NewGuid();
        var message = BuildReservationConfirmed(orderId: orderId, reservationId: newResId, sku: "SKU-B");
        var timestamp = DateTimeOffset.UtcNow;

        var decision = OrderDecider.HandleReservationConfirmed(order, message, timestamp);

        var commits = decision.Messages
            .OfType<IntegrationContracts.ReservationCommitRequested>()
            .ToList();

        // Both the existing and the new reservation must be committed
        commits.Count.ShouldBe(2);
        commits.ShouldContain(c => c.ReservationId == existingRes && c.OrderId == orderId);
        commits.ShouldContain(c => c.ReservationId == newResId && c.OrderId == orderId);
    }

    /// <summary>
    /// When payment is captured and this is the only (first) reservation, one commit must be emitted.
    /// </summary>
    [Fact]
    public void HandleReservationConfirmed_WhenPaymentCaptured_SingleReservation_EmitsOneCommit()
    {
        var orderId = Guid.NewGuid();
        var newResId = Guid.NewGuid();
        var order = BuildOrder(id: orderId, isPaymentCaptured: true, expectedReservationCount: 1);
        var message = BuildReservationConfirmed(orderId: orderId, reservationId: newResId);
        var timestamp = DateTimeOffset.UtcNow;

        var decision = OrderDecider.HandleReservationConfirmed(order, message, timestamp);

        var commits = decision.Messages.OfType<IntegrationContracts.ReservationCommitRequested>().ToList();
        commits.Count.ShouldBe(1);
        commits[0].ReservationId.ShouldBe(newResId);
        commits[0].RequestedAt.ShouldBe(timestamp);
    }

    // ===========================================================================
    // HandleReservationFailed
    // ===========================================================================

    /// <summary>HandleReservationFailed must always set Status to OutOfStock.</summary>
    [Fact]
    public void HandleReservationFailed_SetsStatus_ToOutOfStock()
    {
        var order = BuildOrder(status: OrderStatus.Placed);

        var decision = OrderDecider.HandleReservationFailed(order, BuildReservationFailed(), DateTimeOffset.UtcNow);

        decision.Status.ShouldBe(OrderStatus.OutOfStock);
    }

    /// <summary>
    /// When payment IS captured, HandleReservationFailed must emit RefundRequested
    /// because we cannot fulfill the order.
    /// </summary>
    [Fact]
    public void HandleReservationFailed_WhenPaymentCaptured_EmitsRefundRequested()
    {
        var orderId = Guid.NewGuid();
        var order = BuildOrder(id: orderId, isPaymentCaptured: true, totalAmount: 89.99m);
        var message = BuildReservationFailed(orderId: orderId, sku: "SKU-X", reason: "No stock");
        var timestamp = DateTimeOffset.UtcNow;

        var decision = OrderDecider.HandleReservationFailed(order, message, timestamp);

        var refund = decision.Messages.OfType<RefundRequested>().SingleOrDefault();
        refund.ShouldNotBeNull();
        refund.OrderId.ShouldBe(orderId);
        refund.Amount.ShouldBe(89.99m);
        refund.RequestedAt.ShouldBe(timestamp);
    }

    /// <summary>
    /// When payment is NOT captured, HandleReservationFailed must NOT emit RefundRequested.
    /// </summary>
    [Fact]
    public void HandleReservationFailed_WhenPaymentNotCaptured_DoesNotEmitRefundRequested()
    {
        var order = BuildOrder(isPaymentCaptured: false);

        var decision = OrderDecider.HandleReservationFailed(order, BuildReservationFailed(), DateTimeOffset.UtcNow);

        decision.Messages.OfType<RefundRequested>().ShouldBeEmpty();
    }

    /// <summary>
    /// HandleReservationFailed must emit ReservationReleaseRequested for any
    /// reservations that succeeded before this failure arrived.
    /// </summary>
    [Fact]
    public void HandleReservationFailed_WithExistingReservations_EmitsReleasePerReservation()
    {
        var res1 = Guid.NewGuid();
        var res2 = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var order = BuildOrder(
            id: orderId,
            reservationIds: new Dictionary<Guid, string> { [res1] = "SKU-A", [res2] = "SKU-B" });
        var message = BuildReservationFailed(orderId: orderId, sku: "SKU-C");

        var decision = OrderDecider.HandleReservationFailed(order, message, DateTimeOffset.UtcNow);

        var releases = decision.Messages
            .OfType<IntegrationContracts.ReservationReleaseRequested>()
            .ToList();

        releases.Count.ShouldBe(2);
        releases.ShouldContain(r => r.ReservationId == res1);
        releases.ShouldContain(r => r.ReservationId == res2);
    }

    /// <summary>
    /// HandleReservationFailed with no prior reservations and no payment captured emits no messages.
    /// </summary>
    [Fact]
    public void HandleReservationFailed_NoReservations_NoPaymentCaptured_EmitsNoMessages()
    {
        var order = BuildOrder(isPaymentCaptured: false, reservationIds: new Dictionary<Guid, string>());

        var decision = OrderDecider.HandleReservationFailed(order, BuildReservationFailed(), DateTimeOffset.UtcNow);

        decision.Messages.ShouldBeEmpty();
    }

    // ===========================================================================
    // HandleReservationCommitted — idempotency guard
    // ===========================================================================

    /// <summary>
    /// A duplicate ReservationCommitted (same ReservationId already in CommittedReservationIds)
    /// must return a no-op to prevent double-counting and spurious fulfillment dispatch.
    /// </summary>
    [Fact]
    public void HandleReservationCommitted_DuplicateReservation_ReturnsNoOp()
    {
        var committedRes = Guid.NewGuid();
        var order = BuildOrder(
            isPaymentCaptured: true,
            expectedReservationCount: 1,
            committedReservationIds: [committedRes]);
        var duplicate = BuildReservationCommitted(orderId: order.Id, reservationId: committedRes);

        var decision = OrderDecider.HandleReservationCommitted(order, duplicate, DateTimeOffset.UtcNow);

        decision.Status.ShouldBeNull();
        decision.Messages.ShouldBeEmpty();
        decision.CommittedReservationIds.ShouldBeNull();
    }

    // ===========================================================================
    // HandleReservationCommitted — tracking
    // ===========================================================================

    /// <summary>
    /// A new ReservationCommitted must add the ReservationId to CommittedReservationIds.
    /// </summary>
    [Fact]
    public void HandleReservationCommitted_NewReservation_AddsToCommittedIds()
    {
        var newResId = Guid.NewGuid();
        var order = BuildOrder(
            isPaymentCaptured: true,
            expectedReservationCount: 2,
            reservationIds: new Dictionary<Guid, string> { [newResId] = "SKU-A", [Guid.NewGuid()] = "SKU-B" });
        var message = BuildReservationCommitted(orderId: order.Id, reservationId: newResId);

        var decision = OrderDecider.HandleReservationCommitted(order, message, DateTimeOffset.UtcNow);

        decision.CommittedReservationIds.ShouldNotBeNull();
        decision.CommittedReservationIds!.ShouldContain(newResId);
    }

    /// <summary>
    /// When NOT all reservations are committed, status must be InventoryCommitted (not Fulfilling).
    /// </summary>
    [Fact]
    public void HandleReservationCommitted_NotAllCommitted_SetsStatusToInventoryCommitted()
    {
        var res1 = Guid.NewGuid();
        var res2 = Guid.NewGuid();
        var order = BuildOrder(
            isPaymentCaptured: true,
            expectedReservationCount: 2,
            reservationIds: new Dictionary<Guid, string> { [res1] = "SKU-A", [res2] = "SKU-B" });
        // Committing only res1; res2 not yet committed
        var message = BuildReservationCommitted(orderId: order.Id, reservationId: res1);

        var decision = OrderDecider.HandleReservationCommitted(order, message, DateTimeOffset.UtcNow);

        decision.Status.ShouldBe(OrderStatus.InventoryCommitted);
        decision.Messages.OfType<Messages.Contracts.Fulfillment.FulfillmentRequested>().ShouldBeEmpty();
    }

    // ===========================================================================
    // HandleReservationCommitted — fulfillment dispatch
    // ===========================================================================

    /// <summary>
    /// When ALL reservations are committed AND payment is captured, FulfillmentRequested must be
    /// emitted and status must be Fulfilling.
    /// </summary>
    [Fact]
    public void HandleReservationCommitted_AllCommittedWithPaymentCaptured_EmitsFulfillmentAndSetsFulfilling()
    {
        var resId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var order = BuildOrder(
            id: orderId,
            customerId: customerId,
            isPaymentCaptured: true,
            expectedReservationCount: 1,
            reservationIds: new Dictionary<Guid, string> { [resId] = "SKU-001" },
            lineItems: [new OrderLineItem("SKU-001", 2, 25.00m, 50.00m)]);

        var message = BuildReservationCommitted(orderId: orderId, reservationId: resId);
        var timestamp = DateTimeOffset.UtcNow;

        var decision = OrderDecider.HandleReservationCommitted(order, message, timestamp);

        decision.Status.ShouldBe(OrderStatus.Fulfilling);
        var fulfillment = decision.Messages.OfType<Messages.Contracts.Fulfillment.FulfillmentRequested>().SingleOrDefault();
        fulfillment.ShouldNotBeNull();
        fulfillment.OrderId.ShouldBe(orderId);
        fulfillment.CustomerId.ShouldBe(customerId);
        fulfillment.ShippingMethod.ShouldBe("Ground");
        fulfillment.RequestedAt.ShouldBe(timestamp);
    }

    /// <summary>
    /// FulfillmentRequested must include all line items with SKU and quantity.
    /// </summary>
    [Fact]
    public void HandleReservationCommitted_FulfillmentRequested_IncludesAllLineItems()
    {
        var resId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var order = BuildOrder(
            id: orderId,
            isPaymentCaptured: true,
            expectedReservationCount: 1,
            reservationIds: new Dictionary<Guid, string> { [resId] = "SKU-A" },
            lineItems:
            [
                new OrderLineItem("SKU-A", 3, 10.00m, 30.00m),
                new OrderLineItem("SKU-B", 1, 20.00m, 20.00m),
            ]);
        var message = BuildReservationCommitted(orderId: orderId, reservationId: resId);

        var decision = OrderDecider.HandleReservationCommitted(order, message, DateTimeOffset.UtcNow);

        var fulfillment = decision.Messages.OfType<Messages.Contracts.Fulfillment.FulfillmentRequested>().Single();
        fulfillment.LineItems.Count.ShouldBe(2);
        fulfillment.LineItems.ShouldContain(li => li.Sku == "SKU-A" && li.Quantity == 3);
        fulfillment.LineItems.ShouldContain(li => li.Sku == "SKU-B" && li.Quantity == 1);
    }

    /// <summary>
    /// FulfillmentRequested ShippingAddress must be mapped from the order's ShippingAddress.
    /// Field name mapping: Street→AddressLine1, Street2→AddressLine2, State→StateProvince.
    /// </summary>
    [Fact]
    public void HandleReservationCommitted_FulfillmentRequested_ShippingAddressMappedCorrectly()
    {
        var resId = Guid.NewGuid();
        var order = new Order
        {
            Id = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Status = OrderStatus.PaymentConfirmed,
            IsPaymentCaptured = true,
            ExpectedReservationCount = 1,
            ReservationIds = new Dictionary<Guid, string> { [resId] = "SKU-Z" },
            CommittedReservationIds = [],
            TotalAmount = 50.00m,
            ShippingAddress = new ShippingAddress("99 Elm St", "Unit 5", "Seattle", "WA", "98101", "US"),
            ShippingMethod = "Express",
            LineItems = [new OrderLineItem("SKU-Z", 1, 50.00m, 50.00m)],
        };
        var message = BuildReservationCommitted(orderId: order.Id, reservationId: resId);

        var decision = OrderDecider.HandleReservationCommitted(order, message, DateTimeOffset.UtcNow);

        var addr = decision.Messages.OfType<Messages.Contracts.Fulfillment.FulfillmentRequested>().Single().ShippingAddress;
        addr.AddressLine1.ShouldBe("99 Elm St");
        addr.AddressLine2.ShouldBe("Unit 5");
        addr.City.ShouldBe("Seattle");
        addr.StateProvince.ShouldBe("WA");
        addr.PostalCode.ShouldBe("98101");
        addr.Country.ShouldBe("US");
    }

    /// <summary>
    /// When ALL reservations are committed but payment is NOT yet captured,
    /// FulfillmentRequested must NOT be emitted and status must be InventoryCommitted.
    /// </summary>
    [Fact]
    public void HandleReservationCommitted_AllCommittedButPaymentNotCaptured_DoesNotDispatchFulfillment()
    {
        var resId = Guid.NewGuid();
        var order = BuildOrder(
            isPaymentCaptured: false,
            expectedReservationCount: 1,
            reservationIds: new Dictionary<Guid, string> { [resId] = "SKU-X" });
        var message = BuildReservationCommitted(orderId: order.Id, reservationId: resId);

        var decision = OrderDecider.HandleReservationCommitted(order, message, DateTimeOffset.UtcNow);

        decision.Messages.OfType<Messages.Contracts.Fulfillment.FulfillmentRequested>().ShouldBeEmpty();
        decision.Status.ShouldBe(OrderStatus.InventoryCommitted);
    }

    // ===========================================================================
    // HandleReservationCommitted — terminal state guard (no duplicate fulfillment)
    // ===========================================================================

    /// <summary>
    /// FulfillmentRequested must NOT be emitted if the order is already in a
    /// terminal or post-fulfillment state, even if allCommitted and payment is captured.
    /// </summary>
    [Theory]
    [InlineData(OrderStatus.Fulfilling)]
    [InlineData(OrderStatus.Shipped)]
    [InlineData(OrderStatus.Delivered)]
    [InlineData(OrderStatus.Closed)]
    [InlineData(OrderStatus.Cancelled)]
    [InlineData(OrderStatus.OutOfStock)]
    public void HandleReservationCommitted_InTerminalOrPostFulfillmentState_DoesNotEmitFulfillmentRequested(
        OrderStatus status)
    {
        var resId = Guid.NewGuid();
        var order = BuildOrder(
            status: status,
            isPaymentCaptured: true,
            expectedReservationCount: 1,
            reservationIds: new Dictionary<Guid, string> { [resId] = "SKU-T" });
        var message = BuildReservationCommitted(orderId: order.Id, reservationId: resId);

        var decision = OrderDecider.HandleReservationCommitted(order, message, DateTimeOffset.UtcNow);

        decision.Messages.OfType<Messages.Contracts.Fulfillment.FulfillmentRequested>().ShouldBeEmpty();
    }

    // ===========================================================================
    // HandleReservationReleased
    // ===========================================================================

    /// <summary>HandleReservationReleased must not change order status.</summary>
    [Fact]
    public void HandleReservationReleased_DoesNotChangeStatus()
    {
        var order = BuildOrder(status: OrderStatus.Cancelled);

        var decision = OrderDecider.HandleReservationReleased(order, BuildReservationReleased());

        decision.Status.ShouldBeNull();
    }

    /// <summary>HandleReservationReleased must emit no messages (pure acknowledgement).</summary>
    [Fact]
    public void HandleReservationReleased_EmitsNoMessages()
    {
        var order = BuildOrder();

        var decision = OrderDecider.HandleReservationReleased(order, BuildReservationReleased());

        decision.Messages.ShouldBeEmpty();
    }
}
