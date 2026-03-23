using Marten;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Http;
using Wolverine.Http.Marten;
using Wolverine.Marten;

namespace Returns.Returns;

/// <summary>
/// Inspector starts manual inspection of a received return.
/// Publishes InspectionStarted domain event.
/// </summary>
public static class StartInspectionHandler
{
    public static ProblemDetails Before(StartInspection command, Return? aggregate)
    {
        if (aggregate is null)
            return new ProblemDetails { Detail = "Return not found.", Status = 404 };

        if (aggregate.Status != ReturnStatus.Received)
            return new ProblemDetails
            {
                Detail = $"Return must be in 'Received' state to start inspection. Current state: '{aggregate.Status}'.",
                Status = 409
            };

        return WolverineContinue.NoProblems;
    }

    [WolverinePost("/api/returns/{returnId}/inspection/start")]
    public static InspectionStarted Handle(
        StartInspection command,
        [WriteAggregate] Return aggregate)
    {
        return new InspectionStarted(
            ReturnId: command.ReturnId,
            InspectorId: command.InspectorId,
            StartedAt: DateTimeOffset.UtcNow);
    }
}
