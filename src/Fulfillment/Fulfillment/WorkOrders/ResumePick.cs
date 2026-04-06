using FluentValidation;
using Marten;
using Microsoft.AspNetCore.Mvc;
using Wolverine.Http;

namespace Fulfillment.WorkOrders;

/// <summary>
/// Slice 17: Short pick resolved — alternative bin found.
/// </summary>
public sealed record ResumePick(
    Guid WorkOrderId,
    string Sku,
    int Quantity,
    string AlternativeBinLocation)
{
    public sealed class Validator : AbstractValidator<ResumePick>
    {
        public Validator()
        {
            RuleFor(x => x.WorkOrderId).NotEmpty();
            RuleFor(x => x.Sku).NotEmpty().MaximumLength(100);
            RuleFor(x => x.Quantity).GreaterThan(0);
            RuleFor(x => x.AlternativeBinLocation).NotEmpty().MaximumLength(50);
        }
    }
}

/// <summary>
/// Handler for resuming a pick after a short pick — item found at alternative bin.
/// Appends PickResumed and ItemPicked, and auto-completes if all items are now picked.
/// </summary>
public static class ResumePickHandler
{
    public static async Task<ProblemDetails> Before(
        ResumePick command,
        IDocumentSession session,
        CancellationToken ct)
    {
        var wo = await session.LoadAsync<WorkOrder>(command.WorkOrderId, ct);
        if (wo is null)
            return new ProblemDetails { Detail = "Work order not found", Status = 404 };

        if (wo.Status != WorkOrderStatus.ShortPickPending)
            return new ProblemDetails
            {
                Detail = $"Cannot resume pick for work order in {wo.Status} status. Must be ShortPickPending.",
                Status = 400
            };

        return WolverineContinue.NoProblems;
    }

    public static async Task Handle(
        ResumePick command,
        IDocumentSession session,
        CancellationToken ct)
    {
        var wo = await session.LoadAsync<WorkOrder>(command.WorkOrderId, ct);
        if (wo is null) return;

        var now = DateTimeOffset.UtcNow;

        var pickResumed = new PickResumed(command.Sku, command.AlternativeBinLocation, now);
        session.Events.Append(command.WorkOrderId, pickResumed);
        wo = wo.Apply(pickResumed);

        var itemPicked = new ItemPicked(
            command.Sku,
            command.Quantity,
            command.AlternativeBinLocation,
            wo.AssignedPicker ?? "unknown",
            now);
        session.Events.Append(command.WorkOrderId, itemPicked);

        // Check if all items are now picked (same auto-completion as RecordItemPick)
        var updatedWo = wo.Apply(itemPicked);
        if (updatedWo.AllItemsPicked)
        {
            session.Events.Append(command.WorkOrderId, new PickCompleted(now));
        }
    }
}
