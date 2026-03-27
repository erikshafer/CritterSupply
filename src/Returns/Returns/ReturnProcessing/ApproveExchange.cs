using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Http;
using Wolverine.Http.Marten;
using Wolverine.Marten;

namespace Returns.ReturnProcessing;

/// <summary>
/// CS agent approves an exchange after verifying:
/// 1. Return is in Requested state
/// 2. Return type is Exchange
/// 3. Exchange request details are present
/// 4. Within 30-day return window
///
/// Phase 2: Supports cross-product exchanges with price difference handling.
/// - Replacement costs MORE: appends ExchangeAdditionalPaymentRequired event
/// - Replacement costs LESS: partial refund issued at exchange completion
/// - Replacement costs SAME: no financial adjustment needed
/// </summary>
public sealed record ApproveExchange(Guid ReturnId);

public sealed class ApproveExchangeValidator : AbstractValidator<ApproveExchange>
{
    public ApproveExchangeValidator()
    {
        RuleFor(x => x.ReturnId).NotEmpty().WithMessage("ReturnId is required.");
    }
}

public static class ApproveExchangeHandler
{
    public static ProblemDetails Before(ApproveExchange command, Return? aggregate)
    {
        if (aggregate is null)
            return new ProblemDetails { Detail = "Return not found.", Status = 404 };

        if (aggregate.Type != ReturnType.Exchange)
            return new ProblemDetails
            {
                Detail = "This return is not an exchange request. Use /api/returns/{id}/approve for refund approvals.",
                Status = 409
            };

        if (aggregate.Status != ReturnStatus.Requested)
            return new ProblemDetails
            {
                Detail = $"Exchange is in '{aggregate.Status}' state and cannot be approved. Only exchanges in 'Requested' state can be approved.",
                Status = 409
            };

        if (aggregate.ExchangeRequest is null)
            return new ProblemDetails
            {
                Detail = "Exchange request details are missing.",
                Status = 500
            };

        return WolverineContinue.NoProblems;
    }

    [WolverinePost("/api/returns/{returnId}/approve-exchange")]
    public static async Task<(Events, OutgoingMessages)> Handle(
        ApproveExchange command,
        [WriteAggregate] Return aggregate,
        IMessageBus bus)
    {
        var now = DateTimeOffset.UtcNow;
        var shipByDeadline = now.AddDays(ReturnEligibilityWindow.ReturnWindowDays);

        // Calculate price difference
        var originalTotal = aggregate.Items.Sum(i => i.LineTotal);
        var replacementTotal = aggregate.ExchangeRequest!.ReplacementQuantity * aggregate.ExchangeRequest.ReplacementUnitPrice;
        var priceDifference = originalTotal - replacementTotal;
        // priceDifference > 0 = customer gets refund (replacement cheaper)
        // priceDifference < 0 = customer owes more (replacement more expensive)
        // priceDifference == 0 = no financial adjustment needed

        var events = new Events();
        var outgoing = new OutgoingMessages();

        // Core exchange approval event
        var domainEvent = new ExchangeApproved(
            ReturnId: command.ReturnId,
            PriceDifference: priceDifference,
            ShipByDeadline: shipByDeadline,
            ApprovedAt: now);
        events.Add(domainEvent);

        // Cross-product exchange: append price difference calculated event
        var originalSku = aggregate.Items[0].Sku;
        var isCrossProduct = !string.Equals(originalSku, aggregate.ExchangeRequest.ReplacementSku, StringComparison.OrdinalIgnoreCase);

        if (isCrossProduct)
        {
            events.Add(new CrossProductExchangeRequested(
                ReturnId: command.ReturnId,
                OriginalSku: originalSku,
                ReplacementSku: aggregate.ExchangeRequest.ReplacementSku,
                OriginalUnitPrice: aggregate.Items[0].UnitPrice,
                ReplacementUnitPrice: aggregate.ExchangeRequest.ReplacementUnitPrice,
                Quantity: aggregate.ExchangeRequest.ReplacementQuantity,
                RequestedAt: now));

            events.Add(new ExchangePriceDifferenceCalculated(
                ReturnId: command.ReturnId,
                OriginalTotal: originalTotal,
                ReplacementTotal: replacementTotal,
                PriceDifference: priceDifference,
                CalculatedAt: now));

            outgoing.Add(new Messages.Contracts.Returns.CrossProductExchangeRequested(
                ReturnId: command.ReturnId,
                OrderId: aggregate.OrderId,
                CustomerId: aggregate.CustomerId,
                OriginalSku: originalSku,
                ReplacementSku: aggregate.ExchangeRequest.ReplacementSku,
                OriginalUnitPrice: aggregate.Items[0].UnitPrice,
                ReplacementUnitPrice: aggregate.ExchangeRequest.ReplacementUnitPrice,
                Quantity: aggregate.ExchangeRequest.ReplacementQuantity,
                RequestedAt: now));
        }

        // If replacement costs more, append additional payment required event
        if (priceDifference < 0)
        {
            var amountDue = Math.Abs(priceDifference);

            events.Add(new ExchangeAdditionalPaymentRequired(
                ReturnId: command.ReturnId,
                AmountDue: amountDue,
                RequiredAt: now));

            outgoing.Add(new Messages.Contracts.Returns.ExchangeAdditionalPaymentRequired(
                ReturnId: command.ReturnId,
                OrderId: aggregate.OrderId,
                CustomerId: aggregate.CustomerId,
                AmountDue: amountDue,
                RequiredAt: now));
        }

        // Schedule expiration (same as refund workflow)
        await bus.ScheduleAsync(new ExpireReturn(command.ReturnId), shipByDeadline);

        // Publish exchange approved integration event
        outgoing.Add(new Messages.Contracts.Returns.ExchangeApproved(
            ReturnId: command.ReturnId,
            OrderId: aggregate.OrderId,
            CustomerId: aggregate.CustomerId,
            ReplacementSku: aggregate.ExchangeRequest.ReplacementSku,
            PriceDifference: priceDifference,
            ShipByDeadline: shipByDeadline,
            ApprovedAt: now));

        return (events, outgoing);
    }
}
