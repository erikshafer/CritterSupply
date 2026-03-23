using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Http;
using Wolverine.Http.Marten;
using Wolverine.Marten;

namespace Returns.ReturnProcessing;

/// <summary>
/// CS agent denies an exchange request (out of stock, outside window, or replacement too expensive).
/// </summary>
public sealed record DenyExchange(Guid ReturnId, string Reason, string Message);

public sealed class DenyExchangeValidator : AbstractValidator<DenyExchange>
{
    public DenyExchangeValidator()
    {
        RuleFor(x => x.ReturnId).NotEmpty().WithMessage("ReturnId is required.");
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Denial reason is required.");
        RuleFor(x => x.Message).NotEmpty().WithMessage("Denial message is required.");
    }
}

public static class DenyExchangeHandler
{
    public static ProblemDetails Before(DenyExchange command, Return? aggregate)
    {
        if (aggregate is null)
            return new ProblemDetails { Detail = "Return not found.", Status = 404 };

        if (aggregate.Type != ReturnType.Exchange)
            return new ProblemDetails
            {
                Detail = "This return is not an exchange request.",
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

        var domainEvent = new ExchangeDenied(
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

        return (domainEvent, outgoing);
    }
}
