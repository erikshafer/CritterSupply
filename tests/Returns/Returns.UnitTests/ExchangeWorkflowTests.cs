using Returns.Returns;

namespace Returns.UnitTests;

/// <summary>
/// Unit tests for Exchange workflow domain logic.
/// Tests exchange-specific Apply methods and state transitions.
/// </summary>
public sealed class ExchangeWorkflowTests
{
    private static readonly Guid ReturnId = Guid.CreateVersion7();
    private static readonly Guid OrderId = Guid.CreateVersion7();
    private static readonly Guid CustomerId = Guid.CreateVersion7();

    private static ReturnRequested CreateExchangeRequested(
        string originalSku = "PET-CARRIER-M",
        decimal originalPrice = 50.00m,
        string replacementSku = "PET-CARRIER-L",
        decimal replacementPrice = 50.00m)
    {
        var items = new List<ReturnLineItem>
        {
            new(originalSku, "Pet Carrier (Medium)", 1, originalPrice,
                originalPrice, ReturnReason.Unwanted, "Wrong size")
        };

        var exchangeRequest = new ExchangeRequest(
            ReplacementSku: replacementSku,
            ReplacementQuantity: 1,
            ReplacementUnitPrice: replacementPrice);

        return new ReturnRequested(
            ReturnId, OrderId, CustomerId, items,
            ReturnType.Exchange, exchangeRequest, DateTimeOffset.UtcNow);
    }

    [Fact]
    public void Create_sets_exchange_type_and_request()
    {
        var requested = CreateExchangeRequested();
        var returnAggregate = Return.Create(requested);

        returnAggregate.Type.ShouldBe(ReturnType.Exchange);
        returnAggregate.ExchangeRequest.ShouldNotBeNull();
        returnAggregate.ExchangeRequest.ReplacementSku.ShouldBe("PET-CARRIER-L");
        returnAggregate.ExchangeRequest.ReplacementUnitPrice.ShouldBe(50.00m);
    }

    [Fact]
    public void Apply_ExchangeApproved_transitions_to_Approved_with_price_difference()
    {
        var returnAggregate = Return.Create(CreateExchangeRequested(
            replacementPrice: 40.00m)); // Replacement is $10 cheaper

        var approved = new ExchangeApproved(
            ReturnId,
            PriceDifference: 10.00m, // Customer gets $10 refund
            ShipByDeadline: DateTimeOffset.UtcNow.AddDays(30),
            ApprovedAt: DateTimeOffset.UtcNow);

        var result = returnAggregate.Apply(approved);

        result.Status.ShouldBe(ReturnStatus.Approved);
        result.PriceDifference.ShouldBe(10.00m);
        result.ShipByDeadline.ShouldNotBeNull();
        result.IsTerminal.ShouldBeFalse();
    }

    [Fact]
    public void Apply_ExchangeDenied_transitions_to_Denied_terminal()
    {
        var returnAggregate = Return.Create(CreateExchangeRequested());
        var denied = new ExchangeDenied(
            ReturnId,
            "OutOfStock",
            "Replacement item currently unavailable. Please request a refund or try again later.",
            DateTimeOffset.UtcNow);

        var result = returnAggregate.Apply(denied);

        result.Status.ShouldBe(ReturnStatus.Denied);
        result.DenialReason.ShouldBe("OutOfStock");
        result.IsTerminal.ShouldBeTrue();
    }

    [Fact]
    public void Apply_ExchangeReplacementShipped_transitions_to_ExchangeShipping()
    {
        var returnAggregate = Return.Create(CreateExchangeRequested())
            .Apply(new ExchangeApproved(ReturnId, 0m,
                DateTimeOffset.UtcNow.AddDays(30), DateTimeOffset.UtcNow))
            .Apply(new ReturnReceived(ReturnId, DateTimeOffset.UtcNow))
            .Apply(new InspectionStarted(ReturnId, "inspector-01", DateTimeOffset.UtcNow));

        var result = returnAggregate.Apply(new ExchangeReplacementShipped(
            ReturnId, "SHIP-123", "TRACK-456", DateTimeOffset.UtcNow));

        result.Status.ShouldBe(ReturnStatus.ExchangeShipping);
        result.ReplacementShipmentId.ShouldBe("SHIP-123");
        result.ReplacementShippedAt.ShouldNotBeNull();
        result.IsTerminal.ShouldBeFalse();
    }

    [Fact]
    public void Apply_ExchangeCompleted_transitions_to_Completed_terminal()
    {
        var returnAggregate = Return.Create(CreateExchangeRequested(replacementPrice: 40.00m))
            .Apply(new ExchangeApproved(ReturnId, 10.00m,
                DateTimeOffset.UtcNow.AddDays(30), DateTimeOffset.UtcNow))
            .Apply(new ReturnReceived(ReturnId, DateTimeOffset.UtcNow))
            .Apply(new InspectionStarted(ReturnId, "inspector-01", DateTimeOffset.UtcNow))
            .Apply(new ExchangeReplacementShipped(ReturnId, "SHIP-123", "TRACK-456", DateTimeOffset.UtcNow));

        var result = returnAggregate.Apply(new ExchangeCompleted(
            ReturnId, PriceDifferenceRefund: 10.00m, DateTimeOffset.UtcNow));

        result.Status.ShouldBe(ReturnStatus.Completed);
        result.FinalRefundAmount.ShouldBe(10.00m); // Price difference refund
        result.IsTerminal.ShouldBeTrue();
    }

    [Fact]
    public void Apply_ExchangeRejected_transitions_to_Rejected_terminal()
    {
        var returnAggregate = Return.Create(CreateExchangeRequested())
            .Apply(new ExchangeApproved(ReturnId, 0m,
                DateTimeOffset.UtcNow.AddDays(30), DateTimeOffset.UtcNow))
            .Apply(new ReturnReceived(ReturnId, DateTimeOffset.UtcNow))
            .Apply(new InspectionStarted(ReturnId, "inspector-01", DateTimeOffset.UtcNow));

        var result = returnAggregate.Apply(new ExchangeRejected(
            ReturnId, "Item condition does not qualify for exchange.", DateTimeOffset.UtcNow));

        result.Status.ShouldBe(ReturnStatus.Rejected);
        result.IsTerminal.ShouldBeTrue();
    }

    [Fact]
    public void Exchange_same_price_has_zero_price_difference()
    {
        var returnAggregate = Return.Create(CreateExchangeRequested(
            originalPrice: 50.00m, replacementPrice: 50.00m));

        var approved = new ExchangeApproved(ReturnId, 0m,
            DateTimeOffset.UtcNow.AddDays(30), DateTimeOffset.UtcNow);

        var result = returnAggregate.Apply(approved);

        result.PriceDifference.ShouldBe(0m);
    }

    [Fact]
    public void Exchange_cheaper_replacement_has_positive_price_difference()
    {
        var returnAggregate = Return.Create(CreateExchangeRequested(
            originalPrice: 50.00m, replacementPrice: 40.00m));

        var approved = new ExchangeApproved(ReturnId, 10.00m,
            DateTimeOffset.UtcNow.AddDays(30), DateTimeOffset.UtcNow);

        var result = returnAggregate.Apply(approved);

        result.PriceDifference.ShouldBe(10.00m); // Customer gets $10 refund
    }
}
