using FluentValidation;
using Marten;
using Microsoft.AspNetCore.Mvc;
using Wolverine.Http;

namespace Fulfillment.WorkOrders;

public sealed record StartPacking(
    Guid WorkOrderId)
{
    public sealed class Validator : AbstractValidator<StartPacking>
    {
        public Validator()
        {
            RuleFor(x => x.WorkOrderId).NotEmpty();
        }
    }
}

/// <summary>
/// Handler for starting the packing process.
/// </summary>
public static class StartPackingHandler
{
    public static async Task<ProblemDetails> Before(
        StartPacking command,
        IDocumentSession session,
        CancellationToken ct)
    {
        var wo = await session.LoadAsync<WorkOrder>(command.WorkOrderId, ct);
        if (wo is null)
            return new ProblemDetails { Detail = "Work order not found", Status = 404 };

        if (wo.Status != WorkOrderStatus.PickCompleted)
            return new ProblemDetails
            {
                Detail = $"Cannot start packing for work order in {wo.Status} status. Must be PickCompleted first.",
                Status = 400
            };

        return WolverineContinue.NoProblems;
    }

    public static void Handle(StartPacking command, IDocumentSession session)
    {
        session.Events.Append(command.WorkOrderId,
            new PackingStarted(DateTimeOffset.UtcNow));
    }
}
