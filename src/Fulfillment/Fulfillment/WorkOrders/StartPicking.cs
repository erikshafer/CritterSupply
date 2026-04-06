using FluentValidation;
using Marten;
using Microsoft.AspNetCore.Mvc;
using Wolverine.Http;

namespace Fulfillment.WorkOrders;

public sealed record StartPicking(
    Guid WorkOrderId)
{
    public sealed class Validator : AbstractValidator<StartPicking>
    {
        public Validator()
        {
            RuleFor(x => x.WorkOrderId).NotEmpty();
        }
    }
}

/// <summary>
/// Handler for starting the picking process.
/// </summary>
public static class StartPickingHandler
{
    public static async Task<ProblemDetails> Before(
        StartPicking command,
        IDocumentSession session,
        CancellationToken ct)
    {
        var wo = await session.LoadAsync<WorkOrder>(command.WorkOrderId, ct);
        if (wo is null)
            return new ProblemDetails { Detail = "Work order not found", Status = 404 };

        if (wo.Status != WorkOrderStatus.PickListAssigned)
            return new ProblemDetails
            {
                Detail = $"Cannot start picking for work order in {wo.Status} status. Must be PickListAssigned first.",
                Status = 400
            };

        return WolverineContinue.NoProblems;
    }

    public static void Handle(StartPicking command, IDocumentSession session)
    {
        session.Events.Append(command.WorkOrderId,
            new PickStarted(DateTimeOffset.UtcNow));
    }
}
