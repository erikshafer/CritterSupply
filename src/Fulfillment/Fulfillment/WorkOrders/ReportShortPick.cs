using FluentValidation;
using Marten;
using Microsoft.AspNetCore.Mvc;
using Wolverine.Http;

namespace Fulfillment.WorkOrders;

/// <summary>
/// Slice 16: Item not found at bin during picking.
/// </summary>
public sealed record ReportShortPick(
    Guid WorkOrderId,
    string Sku,
    string BinLocation,
    int ShortageQuantity)
{
    public sealed class Validator : AbstractValidator<ReportShortPick>
    {
        public Validator()
        {
            RuleFor(x => x.WorkOrderId).NotEmpty();
            RuleFor(x => x.Sku).NotEmpty().MaximumLength(100);
            RuleFor(x => x.BinLocation).NotEmpty().MaximumLength(50);
            RuleFor(x => x.ShortageQuantity).GreaterThan(0);
        }
    }
}

/// <summary>
/// Handler for reporting a short pick — item not found at expected bin.
/// Appends ItemNotFoundAtBin and ShortPickDetected, transitions to ShortPickPending.
/// </summary>
public static class ReportShortPickHandler
{
    public static async Task<ProblemDetails> Before(
        ReportShortPick command,
        IDocumentSession session,
        CancellationToken ct)
    {
        var wo = await session.LoadAsync<WorkOrder>(command.WorkOrderId, ct);
        if (wo is null)
            return new ProblemDetails { Detail = "Work order not found", Status = 404 };

        if (wo.Status != WorkOrderStatus.PickStarted)
            return new ProblemDetails
            {
                Detail = $"Cannot report short pick for work order in {wo.Status} status. Must be PickStarted.",
                Status = 400
            };

        if (!wo.LineItems.Any(li => li.Sku == command.Sku))
            return new ProblemDetails
            {
                Detail = $"SKU {command.Sku} is not in this work order",
                Status = 400
            };

        return WolverineContinue.NoProblems;
    }

    public static async Task Handle(ReportShortPick command, IDocumentSession session, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var wo = await session.LoadAsync<WorkOrder>(command.WorkOrderId, ct);
        var expectedQty = wo?.LineItems.FirstOrDefault(li => li.Sku == command.Sku)?.Quantity ?? 0;

        session.Events.Append(command.WorkOrderId,
            new ItemNotFoundAtBin(command.Sku, command.BinLocation, now),
            new ShortPickDetected(command.Sku, expectedQty, command.ShortageQuantity, now));
    }
}
