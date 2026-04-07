using Wolverine.Http;
using Marten;
using Microsoft.AspNetCore.Mvc;

namespace Fulfillment.WorkOrders;

/// <summary>
/// Slice 36: Cold pack special handling command.
/// Applied during packing for temperature-sensitive items.
/// </summary>
public sealed record ApplyColdPack(
    Guid WorkOrderId,
    IReadOnlyList<string> ItemsSkus,
    string PackType);

/// <summary>
/// Slice 36: Cold pack handler.
/// Appends ColdPackApplied between PackingStarted and PackingCompleted.
/// </summary>
public static class ApplyColdPackHandler
{
    public static async Task<ProblemDetails> Before(
        ApplyColdPack command,
        IDocumentSession session,
        CancellationToken ct)
    {
        var wo = await session.LoadAsync<WorkOrder>(command.WorkOrderId, ct);
        if (wo is null)
            return new ProblemDetails { Detail = "Work order not found", Status = 404 };

        if (wo.Status != WorkOrderStatus.PackingStarted)
            return new ProblemDetails
            {
                Detail = $"Cannot apply cold pack for work order in {wo.Status} status. Must be PackingStarted.",
                Status = 400
            };

        return WolverineContinue.NoProblems;
    }

    public static void Handle(
        ApplyColdPack command,
        IDocumentSession session)
    {
        session.Events.Append(command.WorkOrderId,
            new ColdPackApplied(
                command.ItemsSkus,
                command.PackType,
                DateTimeOffset.UtcNow));
    }
}
