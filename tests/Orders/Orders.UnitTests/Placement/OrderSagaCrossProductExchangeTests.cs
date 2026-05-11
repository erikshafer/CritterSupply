using Messages.Contracts.Returns;
using Orders.Placement;

namespace Orders.UnitTests.Placement;

/// <summary>
/// Unit tests for the Order saga's cross-product exchange acknowledgement handlers
/// added in M45.1 / S3 and updated in M47.0 / S2 (per ADR 0062). The Returns BC
/// publishes four cross-product exchange integration messages to the
/// <c>orders-returns-events</c> queue; without these handlers, Wolverine logs
/// "no handler" on every delivery (audit finding).
///
/// Today the four handlers are intentionally minimal:
/// <list type="bullet">
///   <item><c>CrossProductExchangeRequested</c>: tracks the exchange in <c>ActiveReturnIds</c>.</item>
///   <item><c>ExchangeAdditionalPaymentRequired</c>: no-op acknowledger.</item>
///   <item><c>ExchangeAdditionalPaymentCaptured</c>: no-op acknowledger.</item>
///   <item><c>ExchangePartialRefundIssued</c>: no-op acknowledger (M47.0 / S2 — Payments
///         BC owns the actual refund via the Returns ↔ Payments choreography in
///         ADR 0062; forwarding a <c>RefundRequested</c> here would double-refund).</item>
/// </list>
/// Full saga-state branching for "exchange in flight" remains deferred to Slice 3.
/// </summary>
public class OrderSagaCrossProductExchangeTests
{
    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static Order BuildDeliveredOrder(
        Guid? id = null,
        IReadOnlyList<Guid>? activeReturnIds = null,
        bool returnWindowFired = false) =>
        new()
        {
            Id = id ?? Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Status = OrderStatus.Delivered,
            IsPaymentCaptured = true,
            PaymentId = Guid.NewGuid(),
            ActiveReturnIds = activeReturnIds ?? [],
            ReturnWindowFired = returnWindowFired,
            ShippingAddress = new ShippingAddress("1 Main St", null, "City", "ST", "00001", "US"),
            ShippingMethod = "Standard",
            TotalAmount = 120.00m,
            ExpectedReservationCount = 1,
        };

    // ---------------------------------------------------------------------------
    // Handle(CrossProductExchangeRequested)
    // ---------------------------------------------------------------------------

    [Fact]
    public void CrossProductExchangeRequested_Adds_ReturnId_To_ActiveReturns()
    {
        var order = BuildDeliveredOrder();
        var returnId = Guid.NewGuid();

        order.Handle(new CrossProductExchangeRequested(
            ReturnId: returnId,
            OrderId: order.Id,
            CustomerId: order.CustomerId,
            OriginalSku: "PET-CARRIER-M",
            ReplacementSku: "PET-CARRIER-XL",
            OriginalUnitPrice: 50.00m,
            ReplacementUnitPrice: 75.00m,
            Quantity: 1,
            RequestedAt: DateTimeOffset.UtcNow));

        order.ActiveReturnIds.ShouldContain(returnId);
        order.IsCompleted().ShouldBeFalse();
    }

    [Fact]
    public void CrossProductExchangeRequested_Is_Idempotent_For_Same_ReturnId()
    {
        var returnId = Guid.NewGuid();
        var order = BuildDeliveredOrder(activeReturnIds: new[] { returnId }.AsReadOnly());

        order.Handle(new CrossProductExchangeRequested(
            ReturnId: returnId,
            OrderId: order.Id,
            CustomerId: order.CustomerId,
            OriginalSku: "PET-CARRIER-M",
            ReplacementSku: "PET-CARRIER-XL",
            OriginalUnitPrice: 50.00m,
            ReplacementUnitPrice: 75.00m,
            Quantity: 1,
            RequestedAt: DateTimeOffset.UtcNow));

        order.ActiveReturnIds.Count.ShouldBe(1);
    }

    // ---------------------------------------------------------------------------
    // Handle(ExchangeAdditionalPaymentRequired) / Handle(ExchangeAdditionalPaymentCaptured)
    // ---------------------------------------------------------------------------

    [Fact]
    public void ExchangeAdditionalPaymentRequired_Does_Not_Mutate_Saga()
    {
        // No-op acknowledger today. The Payments BC capture is deferred to the remaster.
        var order = BuildDeliveredOrder();
        var statusBefore = order.Status;
        var activeBefore = order.ActiveReturnIds.Count;

        order.Handle(new ExchangeAdditionalPaymentRequired(
            ReturnId: Guid.NewGuid(),
            OrderId: order.Id,
            CustomerId: order.CustomerId,
            AmountDue: 25.00m,
            RequiredAt: DateTimeOffset.UtcNow));

        order.Status.ShouldBe(statusBefore);
        order.ActiveReturnIds.Count.ShouldBe(activeBefore);
    }

    [Fact]
    public void ExchangeAdditionalPaymentCaptured_Does_Not_Mutate_Saga()
    {
        // No-op acknowledger today. Forward-compatible with future Payments wiring.
        var order = BuildDeliveredOrder();
        var statusBefore = order.Status;

        order.Handle(new ExchangeAdditionalPaymentCaptured(
            ReturnId: Guid.NewGuid(),
            OrderId: order.Id,
            CustomerId: order.CustomerId,
            PaymentId: Guid.NewGuid(),
            AmountCaptured: 25.00m,
            Currency: "USD",
            PaymentReference: "txn_test_capture",
            CapturedAt: DateTimeOffset.UtcNow));

        order.Status.ShouldBe(statusBefore);
    }

    // ---------------------------------------------------------------------------
    // Handle(ExchangePartialRefundIssued) — M47.0 / S2: now a no-op acknowledger.
    // Payments BC owns the partial refund directly via the Returns ↔ Payments
    // choreography (see ADR 0062). Forwarding a RefundRequested here would
    // double-refund the customer.
    // ---------------------------------------------------------------------------

    [Fact]
    public void ExchangePartialRefundIssued_Does_Not_Forward_RefundRequested()
    {
        // Regression guard for the M47.0 / S2 fix: the Order saga must NOT
        // forward a RefundRequested to Payments when this integration message
        // arrives, because Payments BC has already issued the refund (that is
        // why this message exists in the first place). See ADR 0062.
        var order = BuildDeliveredOrder();
        var statusBefore = order.Status;

        order.Handle(new ExchangePartialRefundIssued(
            ReturnId: Guid.NewGuid(),
            OrderId: order.Id,
            CustomerId: order.CustomerId,
            OriginalPaymentId: Guid.NewGuid(),
            RefundAmount: 20.00m,
            Currency: "USD",
            TransactionId: "ref_test_refund",
            IssuedAt: DateTimeOffset.UtcNow));

        order.Status.ShouldBe(statusBefore);
    }
}
