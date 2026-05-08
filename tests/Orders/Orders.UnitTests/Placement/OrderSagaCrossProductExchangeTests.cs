using Messages.Contracts.Returns;
using Orders.Placement;
using PaymentContracts = Messages.Contracts.Payments;

namespace Orders.UnitTests.Placement;

/// <summary>
/// Unit tests for the Order saga's cross-product exchange acknowledgement handlers
/// added in M45.1 / S3. The Returns BC publishes four cross-product exchange
/// integration messages to the <c>orders-returns-events</c> queue; without these
/// handlers, Wolverine logs "no handler" on every delivery (audit finding).
///
/// Today the four handlers are intentionally minimal:
/// <list type="bullet">
///   <item><c>CrossProductExchangeRequested</c>: tracks the exchange in <c>ActiveReturnIds</c>.</item>
///   <item><c>ExchangeAdditionalPaymentRequired</c>: no-op acknowledger.</item>
///   <item><c>ExchangeAdditionalPaymentCaptured</c>: no-op acknowledger.</item>
///   <item><c>ExchangePartialRefundIssued</c>: forwards a <c>RefundRequested</c> to Payments.</item>
/// </list>
/// Full cross-BC orchestration (Inventory replacement reservation, Payments delta capture,
/// "exchange in flight" saga state) is deferred to the Returns + Orders remaster per
/// <c>docs/planning/milestones/m45-1-cross-product-exchange-gap-memo.md</c>.
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
            AmountCaptured: 25.00m,
            CapturedAt: DateTimeOffset.UtcNow));

        order.Status.ShouldBe(statusBefore);
    }

    // ---------------------------------------------------------------------------
    // Handle(ExchangePartialRefundIssued) — forwards to Payments BC
    // ---------------------------------------------------------------------------

    [Fact]
    public void ExchangePartialRefundIssued_Forwards_RefundRequested_To_Payments()
    {
        // Closes the most damaging gap from the M45.1 / S3 audit: previously this integration
        // event had no consumer, so the customer never received their partial refund.
        var order = BuildDeliveredOrder();

        var outgoing = order.Handle(new ExchangePartialRefundIssued(
            ReturnId: Guid.NewGuid(),
            OrderId: order.Id,
            CustomerId: order.CustomerId,
            RefundAmount: 20.00m,
            IssuedAt: DateTimeOffset.UtcNow));

        var refund = outgoing.OfType<PaymentContracts.RefundRequested>().SingleOrDefault();
        refund.ShouldNotBeNull();
        refund.OrderId.ShouldBe(order.Id);
        refund.Amount.ShouldBe(20.00m);
        refund.Reason.ShouldContain("Cross-product exchange partial refund");
    }

    [Fact]
    public void ExchangePartialRefundIssued_With_Zero_Amount_Does_Not_Forward_Refund()
    {
        // Defensive guard: only request refund if amount > 0.
        var order = BuildDeliveredOrder();

        var outgoing = order.Handle(new ExchangePartialRefundIssued(
            ReturnId: Guid.NewGuid(),
            OrderId: order.Id,
            CustomerId: order.CustomerId,
            RefundAmount: 0m,
            IssuedAt: DateTimeOffset.UtcNow));

        outgoing.OfType<PaymentContracts.RefundRequested>().ShouldBeEmpty();
    }
}
