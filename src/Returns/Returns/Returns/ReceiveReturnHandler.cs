using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Http;
using Wolverine.Http.Marten;
using Wolverine.Marten;

namespace Returns.Returns;

/// <summary>
/// Warehouse records physical receipt of a return shipment.
/// Publishes ReturnReceived integration event so Customer Experience BC
/// can show "We received your package" — the #1 anxiety-reducer in return flows.
/// </summary>
public static class ReceiveReturnHandler
{
    public static ProblemDetails Before(ReceiveReturn command, Return? aggregate)
    {
        if (aggregate is null)
            return new ProblemDetails { Detail = "Return not found.", Status = 404 };

        if (aggregate.Status != ReturnStatus.Approved)
            return new ProblemDetails
            {
                Detail = $"Return must be in 'Approved' state to be received. Current state: '{aggregate.Status}'.",
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
