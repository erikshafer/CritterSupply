using FluentValidation;
using Fulfillment.Shipments;
using Marten;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Http;

namespace Fulfillment.WorkOrders;

public sealed record VerifyItemAtPack(
    Guid WorkOrderId,
    string Sku,
    int Quantity)
{
    public sealed class Validator : AbstractValidator<VerifyItemAtPack>
    {
        public Validator()
        {
            RuleFor(x => x.WorkOrderId).NotEmpty();
            RuleFor(x => x.Sku).NotEmpty().MaximumLength(100);
            RuleFor(x => x.Quantity).GreaterThan(0);
        }
    }
}

/// <summary>
/// Handler for verifying an item at the pack station. Automatically detects pack completion.
/// When all items are verified, appends DIMWeightCalculated, CartonSelected, and PackingCompleted.
/// On packing completion, cascades GenerateShippingLabel to automatically trigger label generation.
/// </summary>
public static class VerifyItemAtPackHandler
{
    public static async Task<ProblemDetails> Before(
        VerifyItemAtPack command,
        IDocumentSession session,
        CancellationToken ct)
    {
        var wo = await session.LoadAsync<WorkOrder>(command.WorkOrderId, ct);
        if (wo is null)
            return new ProblemDetails { Detail = "Work order not found", Status = 404 };

        if (wo.Status is not (WorkOrderStatus.PackingStarted or WorkOrderStatus.PickCompleted))
            return new ProblemDetails
            {
                Detail = $"Cannot verify item at pack for work order in {wo.Status} status",
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

    public static async Task Handle(
        VerifyItemAtPack command,
        IDocumentSession session,
        IMessageBus bus,
        CancellationToken ct)
    {
        var wo = await session.LoadAsync<WorkOrder>(command.WorkOrderId, ct);
        if (wo is null) return;

        // If packing hasn't started yet (still in PickCompleted), start it automatically
        if (wo.Status == WorkOrderStatus.PickCompleted)
        {
            var packingStarted = new PackingStarted(DateTimeOffset.UtcNow);
            session.Events.Append(command.WorkOrderId, packingStarted);
            wo = wo.Apply(packingStarted);
        }

        var now = DateTimeOffset.UtcNow;
        var verified = new ItemVerifiedAtPack(command.Sku, command.Quantity, now);

        session.Events.Append(command.WorkOrderId, verified);

        // Check if all items are now verified
        var updatedWo = wo.Apply(verified);
        if (updatedWo.AllItemsVerified)
        {
            // Calculate DIM weight (stub — simple weight calculation)
            var totalQty = wo.LineItems.Sum(li => li.Quantity);
            var estimatedWeightLbs = totalQty * 2.5m; // stub: 2.5 lbs per item
            var dimWeight = new DIMWeightCalculated(
                WeightLbs: estimatedWeightLbs,
                LengthIn: 12m, WidthIn: 10m, HeightIn: 8m,
                DimWeightLbs: estimatedWeightLbs * 1.1m,
                CalculatedAt: now);

            var cartonSize = totalQty <= 2 ? "Small" : totalQty <= 5 ? "Medium" : "Large";
            var carton = new CartonSelected(cartonSize, now);

            var billableWeight = Math.Max(dimWeight.WeightLbs, dimWeight.DimWeightLbs);
            var packingCompleted = new PackingCompleted(billableWeight, cartonSize, now);

            session.Events.Append(command.WorkOrderId, dimWeight, carton, packingCompleted);

            // PackingCompleted → GenerateShippingLabel cascading policy.
            // Carrier and service are defaulted (stub) — in production,
            // the routing engine would determine the optimal carrier/service.
            await bus.InvokeAsync(
                new GenerateShippingLabel(wo.ShipmentId, "UPS", "Ground"), ct);
        }
    }
}
