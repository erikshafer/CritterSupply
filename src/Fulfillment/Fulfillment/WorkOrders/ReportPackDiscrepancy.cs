using FluentValidation;
using Marten;
using Microsoft.AspNetCore.Mvc;
using Wolverine.Http;

namespace Fulfillment.WorkOrders;

/// <summary>
/// Discrepancy types for pack station failures.
/// </summary>
public enum DiscrepancyType
{
    WrongItem,
    WeightMismatch
}

/// <summary>
/// Slices 20-21: Wrong item scanned or weight mismatch at pack station.
/// </summary>
public sealed record ReportPackDiscrepancy(
    Guid WorkOrderId,
    string ScannedSku,
    string? ExpectedSku,
    DiscrepancyType DiscrepancyType,
    string Description)
{
    public sealed class Validator : AbstractValidator<ReportPackDiscrepancy>
    {
        public Validator()
        {
            RuleFor(x => x.WorkOrderId).NotEmpty();
            RuleFor(x => x.ScannedSku).NotEmpty().MaximumLength(100);
            RuleFor(x => x.Description).NotEmpty().MaximumLength(500);
        }
    }
}

/// <summary>
/// Handler for reporting a pack discrepancy — wrong item scanned or weight mismatch.
/// Transitions WorkOrder to PackDiscrepancyPending.
/// </summary>
public static class ReportPackDiscrepancyHandler
{
    public static async Task<ProblemDetails> Before(
        ReportPackDiscrepancy command,
        IDocumentSession session,
        CancellationToken ct)
    {
        var wo = await session.LoadAsync<WorkOrder>(command.WorkOrderId, ct);
        if (wo is null)
            return new ProblemDetails { Detail = "Work order not found", Status = 404 };

        if (wo.Status != WorkOrderStatus.PackingStarted)
            return new ProblemDetails
            {
                Detail = $"Cannot report pack discrepancy for work order in {wo.Status} status. Must be PackingStarted.",
                Status = 400
            };

        return WolverineContinue.NoProblems;
    }

    public static void Handle(ReportPackDiscrepancy command, IDocumentSession session)
    {
        var now = DateTimeOffset.UtcNow;

        if (command.DiscrepancyType == DiscrepancyType.WrongItem)
        {
            session.Events.Append(command.WorkOrderId,
                new WrongItemScannedAtPack(
                    command.ExpectedSku ?? "unknown",
                    command.ScannedSku,
                    now));
        }

        session.Events.Append(command.WorkOrderId,
            new PackDiscrepancyDetected(
                command.DiscrepancyType.ToString(),
                command.Description,
                now));
    }
}
