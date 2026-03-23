using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Http;
using Wolverine.Http.Marten;
using Wolverine.Marten;

namespace Returns.Returns;

/// <summary>
/// CS agent denies a return that is in Requested state.
/// Publishes ReturnDenied integration event with customer-facing message.
/// </summary>
public static class DenyReturnHandler
{
    public static ProblemDetails Before(DenyReturn command, Return? aggregate)
    {
        if (aggregate is null)
            return new ProblemDetails { Detail = "Return not found.", Status = 404 };

        if (aggregate.Status != ReturnStatus.Requested)
            return new ProblemDetails
            {
                Detail = $"Return is in '{aggregate.Status}' state and cannot be denied. Only returns in 'Requested' state can be denied.",
                Status = 409
            };

        return WolverineContinue.NoProblems;
    }

    [WolverinePost("/api/returns/{returnId}/deny")]
    public static (ReturnDenied, OutgoingMessages) Handle(
        DenyReturn command,
        [WriteAggregate] Return aggregate)
    {
        var now = DateTimeOffset.UtcNow;
        var denied = new ReturnDenied(
            ReturnId: command.ReturnId,
            Reason: command.Reason,
            Message: command.Message,
            DeniedAt: now);

        var outgoing = new OutgoingMessages();
        outgoing.Add(new Messages.Contracts.Returns.ReturnDenied(
            ReturnId: command.ReturnId,
            OrderId: aggregate.OrderId,
            CustomerId: aggregate.CustomerId,
            Reason: command.Reason,
            Message: command.Message,
            DeniedAt: now));

        return (denied, outgoing);
    }
}
