using Returns.ReturnProcessing;
using IntegrationContracts = Messages.Contracts.Returns;

namespace Returns.UnitTests;

/// <summary>
/// Unit tests for <see cref="ShipReplacementItemHandler"/> covering the M45.1 / S3 fix
/// to emit <see cref="ExchangePartialRefundIssued"/> (both as a domain event on the
/// Return stream and as an integration message to Payments / Orders / Storefront)
/// when a cross-product exchange completes with a cheaper replacement.
///
/// Closes the M45.1 audit finding that <c>Messages.Contracts.Returns.ExchangePartialRefundIssued</c>
/// was registered for publication in <c>Returns.Api/Program.cs</c> but never constructed
/// anywhere in the codebase.
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
    public void Handle_with_cheaper_replacement_emits_ExchangePartialRefundIssued_domain_event()
    {
        // Arrange: replacement is $20 cheaper, so customer is owed a $20 refund.
        var aggregate = BuildExchangeReadyForShipping(originalPrice: 50.00m, replacementPrice: 30.00m);

        // Act
        var (events, _) = ShipReplacementItemHandler.Handle(BuildCommand(), aggregate);

        // Assert: the new domain event is appended to the stream alongside ExchangeReplacementShipped + ExchangeCompleted.
        var refundEvent = events.OfType<ExchangePartialRefundIssued>().SingleOrDefault();
        refundEvent.ShouldNotBeNull();
        refundEvent.ReturnId.ShouldBe(ReturnId);
        refundEvent.RefundAmount.ShouldBe(20.00m);
    }

    [Fact]
    public void Handle_with_cheaper_replacement_emits_integration_ExchangePartialRefundIssued()
    {
        // Arrange
        var aggregate = BuildExchangeReadyForShipping(originalPrice: 50.00m, replacementPrice: 30.00m);

        // Act
        var (_, outgoing) = ShipReplacementItemHandler.Handle(BuildCommand(), aggregate);

        // Assert: the integration contract is published with the order + customer envelope so
        // Payments BC and Storefront receive enough context to act / display.
        var msg = outgoing.OfType<IntegrationContracts.ExchangePartialRefundIssued>().SingleOrDefault();
        msg.ShouldNotBeNull();
        msg.ReturnId.ShouldBe(ReturnId);
        msg.OrderId.ShouldBe(OrderId);
        msg.CustomerId.ShouldBe(CustomerId);
        msg.RefundAmount.ShouldBe(20.00m);
    }

    [Fact]
    public void Handle_with_same_price_replacement_does_NOT_emit_ExchangePartialRefundIssued()
    {
        // Arrange: same-price replacement → no refund owed.
        var aggregate = BuildExchangeReadyForShipping(originalPrice: 50.00m, replacementPrice: 50.00m);

        // Act
        var (events, outgoing) = ShipReplacementItemHandler.Handle(BuildCommand(), aggregate);

        // Assert: no partial refund domain or integration event.
        events.OfType<ExchangePartialRefundIssued>().ShouldBeEmpty();
        outgoing.OfType<IntegrationContracts.ExchangePartialRefundIssued>().ShouldBeEmpty();

        // Sanity: ExchangeCompleted still fires with no refund.
        events.OfType<ExchangeCompleted>().Single().PriceDifferenceRefund.ShouldBeNull();
    }

    [Fact]
    public void Handle_with_more_expensive_replacement_does_NOT_emit_ExchangePartialRefundIssued()
    {
        // Arrange: replacement costs more, customer was charged via ExchangeAdditionalPaymentRequired
        // earlier in the workflow — no refund at completion.
        var aggregate = BuildExchangeReadyForShipping(originalPrice: 50.00m, replacementPrice: 75.00m);

        // Act
        var (events, outgoing) = ShipReplacementItemHandler.Handle(BuildCommand(), aggregate);

        // Assert
        events.OfType<ExchangePartialRefundIssued>().ShouldBeEmpty();
        outgoing.OfType<IntegrationContracts.ExchangePartialRefundIssued>().ShouldBeEmpty();
    }

    [Fact]
    public void Handle_always_emits_ExchangeReplacementShipped_and_ExchangeCompleted()
    {
        // Arrange (any aggregate)
        var aggregate = BuildExchangeReadyForShipping(originalPrice: 50.00m, replacementPrice: 50.00m);

        // Act
        var (events, outgoing) = ShipReplacementItemHandler.Handle(BuildCommand(), aggregate);

        // Assert: pre-existing emissions remain unchanged (regression guard for the M45.1 edit).
        events.OfType<ExchangeReplacementShipped>().Count().ShouldBe(1);
        events.OfType<ExchangeCompleted>().Count().ShouldBe(1);
        outgoing.OfType<IntegrationContracts.ExchangeReplacementShipped>().Count().ShouldBe(1);
        outgoing.OfType<IntegrationContracts.ExchangeCompleted>().Count().ShouldBe(1);
    }
}
