using FluentValidation;
using Marten;
using Microsoft.AspNetCore.Mvc;
using Wolverine.Http;

namespace Fulfillment.WorkOrders;

public sealed record AssignPickList(
    Guid WorkOrderId,
    string PickerId)
{
    public sealed class Validator : AbstractValidator<AssignPickList>
    {
        public Validator()
        {
            RuleFor(x => x.WorkOrderId).NotEmpty();
            RuleFor(x => x.PickerId).NotEmpty().MaximumLength(50);
        }
    }
}

/// <summary>
/// Handler for assigning a pick list to a picker.
/// </summary>
public static class AssignPickListHandler
{
    public static async Task<ProblemDetails> Before(
        AssignPickList command,
        IDocumentSession session,
        CancellationToken ct)
    {
        var wo = await session.LoadAsync<WorkOrder>(command.WorkOrderId, ct);
        if (wo is null)
            return new ProblemDetails { Detail = "Work order not found", Status = 404 };

        if (wo.Status != WorkOrderStatus.WaveReleased)
            return new ProblemDetails
            {
                Detail = $"Cannot assign pick list for work order in {wo.Status} status. Must be WaveReleased first.",
                Status = 400
            };

        return WolverineContinue.NoProblems;
    }

    public static void Handle(AssignPickList command, IDocumentSession session)
    {
        session.Events.Append(command.WorkOrderId,
            new PickListAssigned(command.PickerId, DateTimeOffset.UtcNow));
    }
}
