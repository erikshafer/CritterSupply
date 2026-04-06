using FluentValidation;
using Marten;
using Microsoft.AspNetCore.Mvc;
using Wolverine.Http;

namespace Fulfillment.WorkOrders;

// --- Commands ---

public sealed record ReleaseWave(
    Guid WorkOrderId,
    string WaveId)
{
    public sealed class Validator : AbstractValidator<ReleaseWave>
    {
        public Validator()
        {
            RuleFor(x => x.WorkOrderId).NotEmpty();
            RuleFor(x => x.WaveId).NotEmpty().MaximumLength(50);
        }
    }
}

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

// --- Handlers ---

/// <summary>
/// Handler for releasing a work order into a pick wave.
/// Appends WaveReleased and PickListCreated events.
/// </summary>
public static class ReleaseWaveHandler
{
    public static async Task<ProblemDetails> Before(
        ReleaseWave command,
        IDocumentSession session,
        CancellationToken ct)
    {
        var wo = await session.LoadAsync<WorkOrder>(command.WorkOrderId, ct);
        if (wo is null)
            return new ProblemDetails { Detail = "Work order not found", Status = 404 };

        if (wo.Status != WorkOrderStatus.Created)
            return new ProblemDetails
            {
                Detail = $"Cannot release wave for work order in {wo.Status} status",
                Status = 400
            };

        return WolverineContinue.NoProblems;
    }

    public static void Handle(ReleaseWave command, IDocumentSession session)
    {
        var now = DateTimeOffset.UtcNow;
        session.Events.Append(command.WorkOrderId,
            new WaveReleased(command.WaveId, now),
            new PickListCreated($"PL-{command.WaveId}-{command.WorkOrderId:N}", now));
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

/// <summary>
/// Handler for verifying an item at the pack station. Automatically detects pack completion.
/// When all items are verified, appends DIMWeightCalculated, CartonSelected, and PackingCompleted.
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
        }
    }
}
