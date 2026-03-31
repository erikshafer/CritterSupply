using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine.Http;
using Wolverine.Marten;

namespace Returns.ReturnProcessing;

/// <summary>
/// Inspector starts inspection of a received return.
/// Return moves from ReceivedAwaitingInspection → InspectionInProgress state.
/// </summary>
public sealed record StartInspection(Guid ReturnId, string InspectorId);

public sealed class StartInspectionValidator : AbstractValidator<StartInspection>
{
    public StartInspectionValidator()
    {
        RuleFor(x => x.ReturnId).NotEmpty().WithMessage("ReturnId is required.");
        RuleFor(x => x.InspectorId).NotEmpty().WithMessage("InspectorId is required.");
    }
}

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
    [Authorize]
    public static InspectionStarted Handle(
        StartInspection command,
        [WriteAggregate] Return aggregate)
    {
        var now = DateTimeOffset.UtcNow;

        return new InspectionStarted(
            ReturnId: command.ReturnId,
            InspectorId: command.InspectorId,
            StartedAt: now);
    }
}
