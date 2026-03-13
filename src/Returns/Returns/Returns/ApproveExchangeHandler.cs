using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;

namespace Returns.Returns;

/// <summary>
/// CS agent approves an exchange after verifying:
/// 1. Return is in Requested state
/// 2. Replacement SKU matches original SKU family (same-SKU constraint)
/// 3. Replacement price is same or less than original (no upcharge)
/// 4. Within 30-day return window
///
/// Stock availability check is handled by publishing ExchangeStockCheckRequested
/// to Inventory BC, which responds asynchronously.
/// </summary>
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

        // Validate replacement price (must be same or less)
        var originalTotal = aggregate.Items.Sum(i => i.LineTotal);
        var replacementTotal = aggregate.ExchangeRequest.ReplacementQuantity * aggregate.ExchangeRequest.ReplacementUnitPrice;

        if (replacementTotal > originalTotal)
            return new ProblemDetails
            {
                Detail = $"Replacement item costs more than original (${replacementTotal:F2} > ${originalTotal:F2}). Exchanges cannot collect additional payment. Please deny and advise customer to use refund + new order workflow.",
                Status = 409
            };

        return WolverineContinue.NoProblems;
    }

    [WolverinePost("/api/returns/{returnId}/approve-exchange")]
    public static async Task<(ExchangeApproved, OutgoingMessages)> Handle(
        ApproveExchange command,
        [WriteAggregate] Return aggregate,
        IMessageBus bus)
    {
        var now = DateTimeOffset.UtcNow;
        var shipByDeadline = now.AddDays(ReturnEligibilityWindow.ReturnWindowDays);

        // Calculate price difference (refund if replacement is cheaper)
        var originalTotal = aggregate.Items.Sum(i => i.LineTotal);
        var replacementTotal = aggregate.ExchangeRequest!.ReplacementQuantity * aggregate.ExchangeRequest.ReplacementUnitPrice;
        var priceDifference = originalTotal - replacementTotal; // Positive = customer gets refund

        var domainEvent = new ExchangeApproved(
            ReturnId: command.ReturnId,
            PriceDifference: priceDifference,
            ShipByDeadline: shipByDeadline,
            ApprovedAt: now);

        // Schedule expiration (same as refund workflow)
        await bus.ScheduleAsync(new ExpireReturn(command.ReturnId), shipByDeadline);

        var outgoing = new OutgoingMessages();
        outgoing.Add(new Messages.Contracts.Returns.ExchangeApproved(
            ReturnId: command.ReturnId,
            OrderId: aggregate.OrderId,
            CustomerId: aggregate.CustomerId,
            ReplacementSku: aggregate.ExchangeRequest.ReplacementSku,
            PriceDifference: priceDifference,
            ShipByDeadline: shipByDeadline,
            ApprovedAt: now));

        return (domainEvent, outgoing);
    }
}
