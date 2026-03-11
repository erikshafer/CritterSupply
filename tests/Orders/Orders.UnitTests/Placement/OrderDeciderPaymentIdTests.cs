using Messages.Contracts.Payments;
using Orders.Placement;

namespace Orders.UnitTests.Placement;

/// <summary>
/// Unit tests verifying that <see cref="OrderDecider.HandlePaymentCaptured"/> captures the PaymentId
/// from the <see cref="PaymentCaptured"/> message into the <see cref="OrderDecision"/> (P0.5).
/// The PaymentId is needed by the saga when issuing a RefundRequested for return processing.
/// </summary>
public class OrderDeciderPaymentIdTests
{
    private static Order BuildOrder(OrderStatus status = OrderStatus.Placed) =>
        new()
        {
            Id = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Status = status,
            IsPaymentCaptured = false,
            ShippingAddress = new ShippingAddress("1 Main St", null, "City", "ST", "00001", "US"),
            ShippingMethod = "Standard",
            TotalAmount = 99.99m,
            ExpectedReservationCount = 1,
        };

    private static PaymentCaptured BuildPaymentCaptured(Guid? paymentId = null, Guid? orderId = null) =>
        new(
            paymentId ?? Guid.NewGuid(),
            orderId ?? Guid.NewGuid(),
            99.99m,
            "txn_test_abc123",
            DateTimeOffset.UtcNow);

    // ===========================================================================
    // PaymentId capture
    // ===========================================================================

    /// <summary>
    /// HandlePaymentCaptured must populate PaymentId in the decision
    /// so the Order saga can persist it for use in future RefundRequested messages.
    /// </summary>
    [Fact]
    public void HandlePaymentCaptured_Stores_PaymentId()
    {
        var order = BuildOrder();
        var paymentId = Guid.NewGuid();
        var captured = BuildPaymentCaptured(paymentId: paymentId, orderId: order.Id);

        var decision = OrderDecider.HandlePaymentCaptured(order, captured, DateTimeOffset.UtcNow);

        decision.PaymentId.ShouldNotBeNull();
        decision.PaymentId.ShouldBe(paymentId);
    }

    /// <summary>
    /// PaymentId must match the PaymentId from the PaymentCaptured message exactly —
    /// even when a different PaymentId is provided — ensuring no accidental cross-wiring.
    /// </summary>
    [Theory]
    [InlineData("11111111-1111-1111-1111-111111111111")]
    [InlineData("22222222-2222-2222-2222-222222222222")]
    [InlineData("33333333-3333-3333-3333-333333333333")]
    public void HandlePaymentCaptured_PaymentId_Matches_Message_PaymentId(string paymentIdStr)
    {
        var paymentId = Guid.Parse(paymentIdStr);
        var order = BuildOrder();
        var captured = BuildPaymentCaptured(paymentId: paymentId, orderId: order.Id);

        var decision = OrderDecider.HandlePaymentCaptured(order, captured, DateTimeOffset.UtcNow);

        decision.PaymentId.ShouldBe(paymentId);
    }

    /// <summary>
    /// Sanity check: HandlePaymentCaptured must still set IsPaymentCaptured = true
    /// alongside the PaymentId — both fields are required for the saga to proceed.
    /// </summary>
    [Fact]
    public void HandlePaymentCaptured_Sets_IsPaymentCaptured_Alongside_PaymentId()
    {
        var order = BuildOrder();
        var paymentId = Guid.NewGuid();

        var decision = OrderDecider.HandlePaymentCaptured(
            order,
            BuildPaymentCaptured(paymentId: paymentId),
            DateTimeOffset.UtcNow);

        decision.IsPaymentCaptured.ShouldBe(true);
        decision.PaymentId.ShouldBe(paymentId);
    }
}
