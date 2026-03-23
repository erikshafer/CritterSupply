using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Http;
using Wolverine.Http.Marten;
using Wolverine.Marten;

namespace Returns.Returns;

/// <summary>
/// Warehouse receives the physical return shipment and logs it in the system.
/// Return moves from Approved → ReceivedAwaitingInspection state.
/// </summary>
public sealed record ReceiveReturn(Guid ReturnId);

public sealed class ReceiveReturnValidator : AbstractValidator<ReceiveReturn>
{
    public ReceiveReturnValidator()
    {
        RuleFor(x => x.ReturnId).NotEmpty().WithMessage("ReturnId is required.");
    }
}

public static class ReceiveReturnHandler
{
    public static ProblemDetails Before(ReceiveReturn command, Return? aggregate)
    {
        if (aggregate is null)
            return new ProblemDetails { Detail = "Return not found.", Status = 404 };

        if (aggregate.Status != ReturnStatus.Approved)
            return new ProblemDetails
            {
                Detail = $"Return is in '{aggregate.Status}' state and cannot be received. Only returns in 'Approved' state can be received.",
                Status = 409
            };

        return WolverineContinue.NoProblems;
    }

    [WolverinePost("/api/returns/{returnId}/receive")]
    public static (ReturnReceived, OutgoingMessages) Handle(
        ReceiveReturn command,
        [WriteAggregate] Return aggregate)
    {
        var now = DateTimeOffset.UtcNow;
        var domainEvent = new ReturnReceived(
            ReturnId: command.ReturnId,
            ReceivedAt: now);

        var outgoing = new OutgoingMessages();
        outgoing.Add(new Messages.Contracts.Returns.ReturnReceived(
            ReturnId: command.ReturnId,
            OrderId: aggregate.OrderId,
            CustomerId: aggregate.CustomerId,
            ReceivedAt: now));

        return (domainEvent, outgoing);
    }
}
