using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;

namespace Returns.Returns;

/// <summary>
/// CS agent denies an exchange request with customer-facing reason and message.
/// Common denial reasons:
/// - OutOfStock: Replacement item not available
/// - OutsideReturnWindow: Beyond 30-day eligibility
/// - ReplacementTooExpensive: Replacement costs more (no upcharge collection in v1)
/// - PolicyViolation: Same-SKU constraint violated or other policy issues
/// </summary>
public static class DenyExchangeHandler
{
    public static ProblemDetails Before(DenyExchange command, Return? aggregate)
    {
        if (aggregate is null)
            return new ProblemDetails { Detail = "Return not found.", Status = 404 };

        if (aggregate.Type != ReturnType.Exchange)
            return new ProblemDetails
            {
                Detail = "This return is not an exchange request. Use /api/returns/{id}/deny for refund denials.",
                Status = 409
            };

        if (aggregate.Status != ReturnStatus.Requested)
            return new ProblemDetails
            {
                Detail = $"Exchange is in '{aggregate.Status}' state and cannot be denied. Only exchanges in 'Requested' state can be denied.",
                Status = 409
            };

        return WolverineContinue.NoProblems;
    }

    [WolverinePost("/api/returns/{returnId}/deny-exchange")]
    public static (ExchangeDenied, OutgoingMessages) Handle(
        DenyExchange command,
        [WriteAggregate] Return aggregate)
    {
        var now = DateTimeOffset.UtcNow;
        var denied = new ExchangeDenied(
            ReturnId: command.ReturnId,
            Reason: command.Reason,
            Message: command.Message,
            DeniedAt: now);

        var outgoing = new OutgoingMessages();
        outgoing.Add(new Messages.Contracts.Returns.ExchangeDenied(
            ReturnId: command.ReturnId,
            OrderId: aggregate.OrderId,
            CustomerId: aggregate.CustomerId,
            Reason: command.Reason,
            Message: command.Message,
            DeniedAt: now));

        return (denied, outgoing);
    }
}
