using FluentValidation;
using Marten;
using Microsoft.AspNetCore.Mvc;
using Wolverine.Http;

namespace Fulfillment.WorkOrders;

public sealed record RecordItemPick(
    Guid WorkOrderId,
    string Sku,
    int Quantity,
    string BinLocation)
{
    public sealed class Validator : AbstractValidator<RecordItemPick>
    {
        public Validator()
        {
            RuleFor(x => x.WorkOrderId).NotEmpty();
            RuleFor(x => x.Sku).NotEmpty().MaximumLength(100);
            RuleFor(x => x.Quantity).GreaterThan(0);
            RuleFor(x => x.BinLocation).NotEmpty().MaximumLength(50);
        }
    }
}

/// <summary>
/// Handler for recording an item pick. Automatically detects pick completion.
/// When all items are picked, appends PickCompleted.
/// </summary>
public static class RecordItemPickHandler
{
    public static async Task<ProblemDetails> Before(
        RecordItemPick command,
        IDocumentSession session,
        CancellationToken ct)
    {
        var wo = await session.LoadAsync<WorkOrder>(command.WorkOrderId, ct);
        if (wo is null)
            return new ProblemDetails { Detail = "Work order not found", Status = 404 };

        if (wo.Status is not (WorkOrderStatus.PickStarted or WorkOrderStatus.PickListAssigned))
            return new ProblemDetails
            {
                Detail = $"Cannot record item pick for work order in {wo.Status} status",
                Status = 400
            };

        // Validate SKU is in the work order
        if (!wo.LineItems.Any(li => li.Sku == command.Sku))
            return new ProblemDetails
            {
                Detail = $"SKU {command.Sku} is not in this work order",
                Status = 400
            };

        return WolverineContinue.NoProblems;
    }

    public static async Task Handle(
        RecordItemPick command,
        IDocumentSession session,
        CancellationToken ct)
    {
        var wo = await session.LoadAsync<WorkOrder>(command.WorkOrderId, ct);
        if (wo is null) return;

        // If picking hasn't started yet (still in PickListAssigned), start it automatically
        if (wo.Status == WorkOrderStatus.PickListAssigned)
        {
            var pickStarted = new PickStarted(DateTimeOffset.UtcNow);
            session.Events.Append(command.WorkOrderId, pickStarted);
            wo = wo.Apply(pickStarted);
        }

        var now = DateTimeOffset.UtcNow;
        var itemPicked = new ItemPicked(
            command.Sku,
            command.Quantity,
            command.BinLocation,
            wo.AssignedPicker ?? "unknown",
            now);

        session.Events.Append(command.WorkOrderId, itemPicked);

        // Check if all items are now picked
        var updatedWo = wo.Apply(itemPicked);
        if (updatedWo.AllItemsPicked)
        {
            session.Events.Append(command.WorkOrderId,
                new PickCompleted(now));
        }
    }
}
