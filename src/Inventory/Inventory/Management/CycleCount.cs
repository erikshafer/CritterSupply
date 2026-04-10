using FluentValidation;
using Marten;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Http;

namespace Inventory.Management;

// ---------------------------------------------------------------------------
// Slice 20: InitiateCycleCount — begins a cycle count audit
// ---------------------------------------------------------------------------

public sealed record InitiateCycleCount(
    string Sku,
    string WarehouseId,
    string InitiatedBy)
{
    public sealed class Validator : AbstractValidator<InitiateCycleCount>
    {
        public Validator()
        {
            RuleFor(x => x.Sku).NotEmpty().MaximumLength(50);
            RuleFor(x => x.WarehouseId).NotEmpty().MaximumLength(50);
            RuleFor(x => x.InitiatedBy).NotEmpty().MaximumLength(100);
        }
    }
}

public static class InitiateCycleCountHandler
{
    public static async Task<ProductInventory?> Load(
        InitiateCycleCount command,
        IDocumentSession session,
        CancellationToken ct)
    {
        var inventoryId = InventoryStreamId.Compute(command.Sku, command.WarehouseId);
        return await session.LoadAsync<ProductInventory>(inventoryId, ct);
    }

    public static ProblemDetails Before(
        InitiateCycleCount command,
        ProductInventory? inventory)
    {
        if (inventory is null)
            return new ProblemDetails
            {
                Detail = $"No inventory found for SKU {command.Sku} at warehouse {command.WarehouseId}",
                Status = 404
            };

        return WolverineContinue.NoProblems;
    }

    public static void Handle(
        InitiateCycleCount command,
        ProductInventory inventory,
        IDocumentSession session)
    {
        session.Events.Append(inventory.Id,
            new CycleCountInitiated(
                inventory.Sku,
                inventory.WarehouseId,
                command.InitiatedBy,
                DateTimeOffset.UtcNow));
    }
}

// ---------------------------------------------------------------------------
// Slices 21-22: CompleteCycleCount — records count result, detects discrepancy
// ---------------------------------------------------------------------------

public sealed record CompleteCycleCount(
    string Sku,
    string WarehouseId,
    int PhysicalCount,
    string CountedBy)
{
    public sealed class Validator : AbstractValidator<CompleteCycleCount>
    {
        public Validator()
        {
            RuleFor(x => x.Sku).NotEmpty().MaximumLength(50);
            RuleFor(x => x.WarehouseId).NotEmpty().MaximumLength(50);
            RuleFor(x => x.PhysicalCount).GreaterThanOrEqualTo(0);
            RuleFor(x => x.CountedBy).NotEmpty().MaximumLength(100);
        }
    }
}

public static class CompleteCycleCountHandler
{
    public static async Task<ProductInventory?> Load(
        CompleteCycleCount command,
        IDocumentSession session,
        CancellationToken ct)
    {
        var inventoryId = InventoryStreamId.Compute(command.Sku, command.WarehouseId);
        return await session.LoadAsync<ProductInventory>(inventoryId, ct);
    }

    public static ProblemDetails Before(
        CompleteCycleCount command,
        ProductInventory? inventory)
    {
        if (inventory is null)
            return new ProblemDetails
            {
                Detail = $"No inventory found for SKU {command.Sku} at warehouse {command.WarehouseId}",
                Status = 404
            };

        // If adjustment would push AvailableQuantity negative, reject
        var expectedAvailable = command.PhysicalCount
            - inventory.ReservedQuantity
            - inventory.CommittedQuantity
            - inventory.PickedQuantity;

        if (expectedAvailable < 0)
            return new ProblemDetails
            {
                Detail = $"Cycle count would result in negative available quantity ({expectedAvailable}). " +
                         $"Physical count: {command.PhysicalCount}, " +
                         $"Reserved: {inventory.ReservedQuantity}, " +
                         $"Committed: {inventory.CommittedQuantity}, " +
                         $"Picked: {inventory.PickedQuantity}. " +
                         "Operations manager must investigate before adjusting.",
                Status = 409
            };

        return WolverineContinue.NoProblems;
    }

    public static OutgoingMessages Handle(
        CompleteCycleCount command,
        ProductInventory inventory,
        IDocumentSession session)
    {
        var now = DateTimeOffset.UtcNow;
        var outgoing = new OutgoingMessages();
        var systemCount = inventory.TotalOnHand;

        session.Events.Append(inventory.Id,
            new CycleCountCompleted(
                inventory.Sku,
                inventory.WarehouseId,
                command.PhysicalCount,
                systemCount,
                command.CountedBy,
                now));

        // Calculate the expected available quantity after accounting for all allocations
        var expectedAvailable = command.PhysicalCount
            - inventory.ReservedQuantity
            - inventory.CommittedQuantity
            - inventory.PickedQuantity;

        var delta = expectedAvailable - inventory.AvailableQuantity;

        if (delta != 0)
        {
            // Discrepancy found — adjust inventory
            session.Events.Append(inventory.Id,
                new StockDiscrepancyFound(
                    inventory.Sku,
                    inventory.WarehouseId,
                    inventory.AvailableQuantity,
                    expectedAvailable,
                    DiscrepancyType.CycleCount,
                    delta > 0
                        ? $"Cycle count surplus: found {delta} more than expected"
                        : $"Cycle count shortage: found {-delta} fewer than expected",
                    now));

            session.Events.Append(inventory.Id,
                new InventoryAdjusted(
                    inventory.Sku,
                    inventory.WarehouseId,
                    delta,
                    $"Cycle count adjustment by {command.CountedBy}",
                    command.CountedBy,
                    now));

            // Low stock alert — inline check (same pattern as S1)
            var newAvailable = inventory.AvailableQuantity + delta;
            if (LowStockPolicy.CrossedThresholdDownward(inventory.AvailableQuantity, newAvailable))
            {
                session.Events.Append(inventory.Id,
                    new LowStockThresholdBreached(
                        inventory.Sku,
                        inventory.WarehouseId,
                        inventory.AvailableQuantity,
                        newAvailable,
                        LowStockPolicy.DefaultThreshold,
                        now));
            }
        }

        return outgoing;
    }
}
