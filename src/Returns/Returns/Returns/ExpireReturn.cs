using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Http;
using Wolverine.Http.Marten;
using Wolverine.Marten;

namespace Returns.Returns;

/// <summary>
/// Scheduled message handler for expiring returns that exceed the ship-by deadline.
/// Return moves from Approved → Expired state.
/// </summary>
public sealed record ExpireReturn(Guid ReturnId);

public sealed class ExpireReturnValidator : AbstractValidator<ExpireReturn>
{
    public ExpireReturnValidator()
    {
        RuleFor(x => x.ReturnId).NotEmpty().WithMessage("ReturnId is required.");
    }
}

public static class ExpireReturnHandler
{
    public static ProblemDetails Before(ExpireReturn command, Return? aggregate)
    {
        if (aggregate is null)
            return new ProblemDetails { Detail = "Return not found.", Status = 404 };

        if (aggregate.Status != ReturnStatus.Approved)
            return new ProblemDetails
            {
                Detail = $"Return must be in 'Approved' state to expire. Current state: '{aggregate.Status}'.",
                Status = 409
            };

        return WolverineContinue.NoProblems;
    }

    public static (ReturnExpired, OutgoingMessages) Handle(
        ExpireReturn command,
        [WriteAggregate] Return aggregate)
    {
        var now = DateTimeOffset.UtcNow;
        var domainEvent = new ReturnExpired(
            ReturnId: command.ReturnId,
            ExpiredAt: now);

        var outgoing = new OutgoingMessages();
        outgoing.Add(new Messages.Contracts.Returns.ReturnExpired(
            ReturnId: command.ReturnId,
            OrderId: aggregate.OrderId,
            CustomerId: aggregate.CustomerId,
            ExpiredAt: now));

        return (domainEvent, outgoing);
    }
}
