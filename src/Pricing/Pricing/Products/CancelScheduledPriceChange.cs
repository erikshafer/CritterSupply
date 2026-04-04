using Marten;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Wolverine.Http;

namespace Pricing.Products;

/// <summary>
/// Command: Cancel a pending scheduled price change before it activates.
/// PricingManager role only. Stale Wolverine message discarded by ActivateScheduledPriceChangeHandler guard.
/// </summary>
public sealed record CancelScheduledPriceChange(string Sku, Guid ScheduleId);

/// <summary>
/// DELETE endpoints carry no request body; route parameters are bound directly.
/// Uses a single-method async handler (consistent with other DELETE handlers in the codebase).
/// Business guards are inline because there is no body to separate Load/Before/Handle for.
/// </summary>
public static class CancelScheduledPriceChangeHandler
{
    [WolverineDelete("/api/pricing/products/{sku}/schedule/{scheduleId}")]
    [Authorize(Policy = "PricingManager")]
    public static async Task<IResult> HandleAsync(
        string sku,
        Guid scheduleId,
        IDocumentSession session,
        CancellationToken ct)
    {
        var streamId = ProductPrice.StreamId(sku);
        var price = await session.Events.AggregateStreamAsync<ProductPrice>(streamId, token: ct);

        if (price is null)
            return Results.NotFound(new { message = $"Product '{sku}' not found in Pricing BC." });

        if (price.PendingSchedule is null)
            return Results.NotFound(new { message = "No pending scheduled price change found for this product." });

        if (price.PendingSchedule.ScheduleId != scheduleId)
            return Results.NotFound(new
            {
                message = "Schedule ID does not match the pending schedule.",
                expectedScheduleId = price.PendingSchedule.ScheduleId,
                providedScheduleId = scheduleId
            });

        var evt = new ScheduledPriceChangeCancelled(
            ProductPriceId: streamId,
            Sku: sku.ToUpperInvariant(),
            ScheduleId: scheduleId,
            CancellationReason: "Cancelled by PricingManager",
            CancelledBy: Guid.Empty, // TODO: Extract from JWT claim
            CancelledAt: DateTimeOffset.UtcNow);

        session.Events.Append(streamId, evt);

        return Results.Ok(new
        {
            sku = sku.ToUpperInvariant(),
            scheduleId,
            message = "Scheduled price change cancelled successfully"
        });
    }
}

