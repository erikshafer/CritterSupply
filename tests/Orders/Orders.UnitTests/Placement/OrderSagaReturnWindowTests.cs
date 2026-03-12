using Messages.Contracts.Returns;
using Messages.Contracts.Payments;
using Orders.Placement;
using FulfillmentMessages = Messages.Contracts.Fulfillment;

namespace Orders.UnitTests.Placement;

/// <summary>
/// Unit tests for the Order saga's return window handling (P0.5).
/// Tests the saga's Handle() methods directly since they are POCO methods
/// that only modify state and return outgoing messages — no infrastructure needed.
/// </summary>
public class OrderSagaReturnWindowTests
{
    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static Order BuildDeliveredOrder(
        Guid? id = null,
        bool isReturnInProgress = false,
        bool returnWindowFired = false,
        Guid? activeReturnId = null) =>
        new()
        {
            Id = id ?? Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Status = OrderStatus.Delivered,
            IsPaymentCaptured = true,
            PaymentId = Guid.NewGuid(),
            IsReturnInProgress = isReturnInProgress,
            ActiveReturnId = activeReturnId,
            ReturnWindowFired = returnWindowFired,
            ShippingAddress = new ShippingAddress("1 Main St", null, "City", "ST", "00001", "US"),
            ShippingMethod = "Standard",
            TotalAmount = 120.00m,
            ExpectedReservationCount = 1,
        };

    // ===========================================================================
    // Handle(ReturnWindowExpired)
    // ===========================================================================

    /// <summary>
    /// When ReturnWindowExpired fires and no return is in progress,
    /// the saga must close immediately: Status → Closed, saga marked completed.
    /// </summary>
    [Fact]
    public void ReturnWindowExpired_With_No_Return_In_Progress_Closes_Saga()
    {
        var order = BuildDeliveredOrder(isReturnInProgress: false);

        order.Handle(new ReturnWindowExpired(order.Id));

        order.ReturnWindowFired.ShouldBeTrue();
        order.Status.ShouldBe(OrderStatus.Closed);
        order.IsCompleted().ShouldBeTrue();
    }

    /// <summary>
    /// When ReturnWindowExpired fires while a return is in progress,
    /// the saga must NOT close — it stays open waiting for ReturnCompleted/ReturnDenied.
    /// ReturnWindowFired is set to true so the saga knows to close when the return resolves.
    /// </summary>
    [Fact]
    public void ReturnWindowExpired_With_Return_In_Progress_Does_Not_Close_Saga()
    {
        var order = BuildDeliveredOrder(isReturnInProgress: true, activeReturnId: Guid.NewGuid());

        order.Handle(new ReturnWindowExpired(order.Id));

        order.ReturnWindowFired.ShouldBeTrue();
        order.Status.ShouldBe(OrderStatus.Delivered); // not closed
        order.IsCompleted().ShouldBeFalse();
    }

    // ===========================================================================
    // Handle(ReturnRequested)
    // ===========================================================================

    /// <summary>
    /// ReturnRequested must set IsReturnInProgress = true and capture the ActiveReturnId.
    /// This prevents premature saga closure if ReturnWindowExpired fires concurrently.
    /// </summary>
    [Fact]
    public void ReturnRequested_Sets_IsReturnInProgress_And_ActiveReturnId()
    {
        var order = BuildDeliveredOrder();
        var returnId = Guid.NewGuid();

        order.Handle(new ReturnRequested(returnId, order.Id, order.CustomerId, DateTimeOffset.UtcNow));

        order.IsReturnInProgress.ShouldBeTrue();
        order.ActiveReturnId.ShouldBe(returnId);
    }

    // ===========================================================================
    // Handle(ReturnCompleted)
    // ===========================================================================

    /// <summary>
    /// ReturnCompleted must close the saga, emit a RefundRequested for the final refund amount,
    /// and clear the in-progress flag. This applies regardless of whether the return window fired.
    /// </summary>
    [Fact]
    public void ReturnCompleted_Closes_Saga_And_Emits_RefundRequested()
    {
        var order = BuildDeliveredOrder(isReturnInProgress: true, returnWindowFired: true);
        var returnId = Guid.NewGuid();
        const decimal refundAmount = 85.50m;

        var outgoing = order.Handle(new ReturnCompleted(returnId, order.Id, order.CustomerId, refundAmount, [], DateTimeOffset.UtcNow));

        // Saga state
        order.IsReturnInProgress.ShouldBeFalse();
        order.Status.ShouldBe(OrderStatus.Closed);
        order.IsCompleted().ShouldBeTrue();

        // Refund must be requested for the approved return amount
        var refundRequests = outgoing.OfType<RefundRequested>().ToList();
        refundRequests.Count.ShouldBe(1);
        refundRequests[0].OrderId.ShouldBe(order.Id);
        refundRequests[0].Amount.ShouldBe(refundAmount);
    }

    /// <summary>
    /// ReturnCompleted before the return window fires must also close the saga.
    /// (Customer returned and refund approved within the 30-day window.)
    /// </summary>
    [Fact]
    public void ReturnCompleted_Before_Window_Fires_Also_Closes_Saga()
    {
        var order = BuildDeliveredOrder(isReturnInProgress: true, returnWindowFired: false);
        var returnId = Guid.NewGuid();

        var outgoing = order.Handle(new ReturnCompleted(returnId, order.Id, order.CustomerId, 50m, [], DateTimeOffset.UtcNow));

        order.Status.ShouldBe(OrderStatus.Closed);
        order.IsCompleted().ShouldBeTrue();
        outgoing.OfType<RefundRequested>().ShouldNotBeEmpty();
    }

    // ===========================================================================
    // Handle(ReturnDenied)
    // ===========================================================================

    /// <summary>
    /// When a return is denied AFTER the return window has already fired,
    /// the saga must close immediately — there is nothing else to wait for.
    /// </summary>
    [Fact]
    public void ReturnDenied_After_Window_Fires_Closes_Saga()
    {
        var order = BuildDeliveredOrder(
            isReturnInProgress: true,
            returnWindowFired: true,
            activeReturnId: Guid.NewGuid());

        order.Handle(new ReturnDenied(Guid.NewGuid(), order.Id, order.CustomerId, "Items not eligible for return", null, DateTimeOffset.UtcNow));

        order.IsReturnInProgress.ShouldBeFalse();
        order.ActiveReturnId.ShouldBeNull();
        order.Status.ShouldBe(OrderStatus.Closed);
        order.IsCompleted().ShouldBeTrue();
    }

    /// <summary>
    /// When a return is denied BEFORE the return window fires, the saga must stay open.
    /// It waits for ReturnWindowExpired to close (normal case: customer tried to return,
    /// was denied, and the window has not yet expired).
    /// </summary>
    [Fact]
    public void ReturnDenied_Before_Window_Fires_Does_Not_Close_Saga()
    {
        var order = BuildDeliveredOrder(
            isReturnInProgress: true,
            returnWindowFired: false,
            activeReturnId: Guid.NewGuid());

        order.Handle(new ReturnDenied(Guid.NewGuid(), order.Id, order.CustomerId, "Items not eligible for return", null, DateTimeOffset.UtcNow));

        order.IsReturnInProgress.ShouldBeFalse();
        order.ActiveReturnId.ShouldBeNull();
        order.Status.ShouldBe(OrderStatus.Delivered); // still open
        order.IsCompleted().ShouldBeFalse();
    }

    // ===========================================================================
    // Handle(ReturnRejected) — inspection failure
    // ===========================================================================

    /// <summary>
    /// When a return is rejected (inspection failure) AFTER the return window has already fired,
    /// the saga must close immediately — same pattern as ReturnDenied.
    /// </summary>
    [Fact]
    public void ReturnRejected_After_Window_Fires_Closes_Saga()
    {
        var order = BuildDeliveredOrder(
            isReturnInProgress: true,
            returnWindowFired: true,
            activeReturnId: Guid.NewGuid());

        order.Handle(new Messages.Contracts.Returns.ReturnRejected(
            Guid.NewGuid(), order.Id, order.CustomerId,
            "Inspection found items in unacceptable condition.", [], DateTimeOffset.UtcNow));

        order.IsReturnInProgress.ShouldBeFalse();
        order.ActiveReturnId.ShouldBeNull();
        order.Status.ShouldBe(OrderStatus.Closed);
        order.IsCompleted().ShouldBeTrue();
    }

    /// <summary>
    /// When a return is rejected BEFORE the return window fires, the saga must stay open.
    /// The customer's return failed inspection, but the window hasn't expired yet.
    /// </summary>
    [Fact]
    public void ReturnRejected_Before_Window_Fires_Does_Not_Close_Saga()
    {
        var order = BuildDeliveredOrder(
            isReturnInProgress: true,
            returnWindowFired: false,
            activeReturnId: Guid.NewGuid());

        order.Handle(new Messages.Contracts.Returns.ReturnRejected(
            Guid.NewGuid(), order.Id, order.CustomerId,
            "Inspection found items in unacceptable condition.", [], DateTimeOffset.UtcNow));

        order.IsReturnInProgress.ShouldBeFalse();
        order.ActiveReturnId.ShouldBeNull();
        order.Status.ShouldBe(OrderStatus.Delivered); // still open
        order.IsCompleted().ShouldBeFalse();
    }

    // ===========================================================================
    // Handle(ReturnExpired) — return ship-by deadline missed
    // ===========================================================================

    /// <summary>
    /// When a return expires AFTER the return window has already fired,
    /// the saga must close immediately.
    /// </summary>
    [Fact]
    public void ReturnExpired_After_Window_Fires_Closes_Saga()
    {
        var order = BuildDeliveredOrder(
            isReturnInProgress: true,
            returnWindowFired: true,
            activeReturnId: Guid.NewGuid());

        order.Handle(new Messages.Contracts.Returns.ReturnExpired(
            Guid.NewGuid(), order.Id, order.CustomerId, DateTimeOffset.UtcNow));

        order.IsReturnInProgress.ShouldBeFalse();
        order.ActiveReturnId.ShouldBeNull();
        order.Status.ShouldBe(OrderStatus.Closed);
        order.IsCompleted().ShouldBeTrue();
    }

    /// <summary>
    /// When a return expires BEFORE the return window fires, the saga must stay open.
    /// </summary>
    [Fact]
    public void ReturnExpired_Before_Window_Fires_Does_Not_Close_Saga()
    {
        var order = BuildDeliveredOrder(
            isReturnInProgress: true,
            returnWindowFired: false,
            activeReturnId: Guid.NewGuid());

        order.Handle(new Messages.Contracts.Returns.ReturnExpired(
            Guid.NewGuid(), order.Id, order.CustomerId, DateTimeOffset.UtcNow));

        order.IsReturnInProgress.ShouldBeFalse();
        order.ActiveReturnId.ShouldBeNull();
        order.Status.ShouldBe(OrderStatus.Delivered); // still open
        order.IsCompleted().ShouldBeFalse();
    }

    // ===========================================================================
    // Handle(ShipmentDelivered) — DeliveredAt persistence
    // ===========================================================================

    /// <summary>
    /// ShipmentDelivered must persist DeliveredAt on the saga state.
    /// This timestamp is used by the BFF for "Return by {date}" display.
    /// </summary>
    [Fact]
    public void ShipmentDelivered_Persists_DeliveredAt()
    {
        var orderId = Guid.NewGuid();
        var order = new Order
        {
            Id = orderId,
            CustomerId = Guid.NewGuid(),
            Status = OrderStatus.Shipped,
            IsPaymentCaptured = true,
            PaymentId = Guid.NewGuid(),
            ShippingAddress = new ShippingAddress("1 Main St", null, "City", "ST", "00001", "US"),
            ShippingMethod = "Standard",
            TotalAmount = 120.00m,
            ExpectedReservationCount = 1,
        };

        var deliveredAt = DateTimeOffset.UtcNow;
        var shipmentId = Guid.NewGuid();

        order.Handle(new FulfillmentMessages.ShipmentDelivered(orderId, shipmentId, deliveredAt));

        order.DeliveredAt.ShouldNotBeNull();
        order.DeliveredAt.ShouldBe(deliveredAt);
        order.Status.ShouldBe(OrderStatus.Delivered);
    }
}
