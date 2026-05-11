using Returns.ReturnProcessing;
using PaymentsContracts = Messages.Contracts.Payments;
using ReturnsContracts = Messages.Contracts.Returns;

namespace Returns.UnitTests;

/// <summary>
/// Unit tests for <see cref="ShipReplacementItemHandler"/>.
///
/// <para>
/// Updated in M47.0 / Slice 2 (per ADR 0062): the handler no longer constructs the
/// <see cref="ExchangePartialRefundIssued"/> domain event nor the public
/// <see cref="ReturnsContracts.ExchangePartialRefundIssued"/> integration message
/// directly — those are now appended / republished by
/// <c>Returns.Integration.ExchangePartialRefundIssuedHandler</c> when the Payments BC
/// replies that the refund has actually been issued. ShipReplacementItem instead
/// publishes an <see cref="PaymentsContracts.ExchangePartialRefundRequested"/> to
/// trigger Payments' refund handler.
/// </para>
/// </summary>
public sealed class ShipReplacementItemHandlerTests
{
    private static readonly Guid ReturnId = Guid.CreateVersion7();
    private static readonly Guid OrderId = Guid.CreateVersion7();
    private static readonly Guid CustomerId = Guid.CreateVersion7();

    private static Return BuildExchangeReadyForShipping(decimal originalPrice, decimal replacementPrice)
    {
        var items = new List<ReturnLineItem>
        {
            new("PET-CARRIER-M", "Pet Carrier (Medium)", 1, originalPrice,
                originalPrice, ReturnReason.Unwanted, "Wrong size")
        };
        var exchange = new ExchangeRequest("PET-CARRIER-L", 1, replacementPrice);
        var requested = new ReturnRequested(
            ReturnId, OrderId, CustomerId, items,
            ReturnType.Exchange, exchange, DateTimeOffset.UtcNow);

        // priceDifference convention: originalTotal - replacementTotal (positive ⇒ refund owed).
        var priceDifference = originalPrice - replacementPrice;

        return Return.Create(requested)
            .Apply(new ExchangeApproved(ReturnId, priceDifference,
                DateTimeOffset.UtcNow.AddDays(30), DateTimeOffset.UtcNow))
            .Apply(new ReturnReceived(ReturnId, DateTimeOffset.UtcNow))
            .Apply(new InspectionStarted(ReturnId, "inspector-01", DateTimeOffset.UtcNow));
    }

    private static ShipReplacementItem BuildCommand() =>
        new(ReturnId, "SHIP-123", "TRACK-456");

    [Fact]
    public void Handle_with_cheaper_replacement_publishes_ExchangePartialRefundRequested_to_Payments()
    {
        // M47.0 / S2: the refund is now driven by Payments BC. ShipReplacementItem
        // publishes a request; the actual refund and the public ExchangePartialRefundIssued
        // are emitted by Payments + the Returns integration handler when Payments replies.
        var aggregate = BuildExchangeReadyForShipping(originalPrice: 50.00m, replacementPrice: 30.00m);

        var (_, outgoing) = ShipReplacementItemHandler.Handle(BuildCommand(), aggregate);

        var request = outgoing.OfType<PaymentsContracts.ExchangePartialRefundRequested>().SingleOrDefault();
        request.ShouldNotBeNull();
        request.ReturnId.ShouldBe(ReturnId);
        request.OrderId.ShouldBe(OrderId);
        request.CustomerId.ShouldBe(CustomerId);
        request.RefundAmount.ShouldBe(20.00m);
    }

    [Fact]
    public void Handle_with_cheaper_replacement_does_NOT_emit_partial_refund_domain_event()
    {
        // Regression guard for the M47.0 / S2 refactor: the domain event for the issued refund
        // must come from the Payments reply path, not from ShipReplacementItem. Otherwise the
        // Return aggregate would record the refund before Payments has actually moved the money.
        var aggregate = BuildExchangeReadyForShipping(originalPrice: 50.00m, replacementPrice: 30.00m);

        var (events, outgoing) = ShipReplacementItemHandler.Handle(BuildCommand(), aggregate);

        events.OfType<ExchangePartialRefundIssued>().ShouldBeEmpty();
        outgoing.OfType<ReturnsContracts.ExchangePartialRefundIssued>().ShouldBeEmpty();
    }

    [Fact]
    public void Handle_with_same_price_replacement_does_NOT_request_partial_refund()
    {
        // Same-price replacement → no refund owed → no Payments request emitted.
        var aggregate = BuildExchangeReadyForShipping(originalPrice: 50.00m, replacementPrice: 50.00m);

        var (events, outgoing) = ShipReplacementItemHandler.Handle(BuildCommand(), aggregate);

        outgoing.OfType<PaymentsContracts.ExchangePartialRefundRequested>().ShouldBeEmpty();
        events.OfType<ExchangePartialRefundIssued>().ShouldBeEmpty();

        // Sanity: ExchangeCompleted still fires with no refund.
        events.OfType<ExchangeCompleted>().Single().PriceDifferenceRefund.ShouldBeNull();
    }

    [Fact]
    public void Handle_with_more_expensive_replacement_does_NOT_request_partial_refund()
    {
        // Replacement costs more → customer was charged via the delta-capture path
        // earlier in the workflow → no refund at completion.
        var aggregate = BuildExchangeReadyForShipping(originalPrice: 50.00m, replacementPrice: 75.00m);

        var (events, outgoing) = ShipReplacementItemHandler.Handle(BuildCommand(), aggregate);

        outgoing.OfType<PaymentsContracts.ExchangePartialRefundRequested>().ShouldBeEmpty();
        events.OfType<ExchangePartialRefundIssued>().ShouldBeEmpty();
    }

    [Fact]
    public void Handle_always_emits_ExchangeReplacementShipped_and_ExchangeCompleted()
    {
        // Pre-existing emissions remain unchanged (regression guard for the M47.0 / S2 refactor).
        var aggregate = BuildExchangeReadyForShipping(originalPrice: 50.00m, replacementPrice: 50.00m);

        var (events, outgoing) = ShipReplacementItemHandler.Handle(BuildCommand(), aggregate);

        events.OfType<ExchangeReplacementShipped>().Count().ShouldBe(1);
        events.OfType<ExchangeCompleted>().Count().ShouldBe(1);
        outgoing.OfType<ReturnsContracts.ExchangeReplacementShipped>().Count().ShouldBe(1);
        outgoing.OfType<ReturnsContracts.ExchangeCompleted>().Count().ShouldBe(1);
    }
}
