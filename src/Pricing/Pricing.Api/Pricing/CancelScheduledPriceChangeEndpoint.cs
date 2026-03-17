using Marten;
using Microsoft.AspNetCore.Authorization;
using Pricing.Products;
using Wolverine.Http;

namespace Pricing.Api.Pricing;

/// <summary>
/// HTTP endpoint: Cancel a scheduled price change before it activates.
/// PricingManager role can cancel pending schedules.
/// </summary>
public static class CancelScheduledPriceChangeEndpoint
{
    [WolverineDelete("/api/pricing/products/{sku}/schedule/{scheduleId}")]
    [Authorize(Policy = "PricingManager")]
    public static async Task<IResult> CancelScheduledPriceChange(
        string sku,
        Guid scheduleId,
        IDocumentSession session,
        CancellationToken ct)
    {
        var streamId = ProductPrice.StreamId(sku);
        var aggregate = await session.Events.AggregateStreamAsync<ProductPrice>(streamId, token: ct);

        if (aggregate is null)
        {
            return Results.NotFound(new { message = $"Product with SKU '{sku}' not found in Pricing BC." });
        }

        if (aggregate.PendingSchedule is null)
        {
            return Results.NotFound(new { message = "No pending scheduled price change found for this product." });
        }

        if (aggregate.PendingSchedule.ScheduleId != scheduleId)
        {
            return Results.NotFound(new
            {
                message = "Schedule ID does not match the pending schedule.",
                expectedScheduleId = aggregate.PendingSchedule.ScheduleId,
                providedScheduleId = scheduleId
            });
        }

        var evt = new ScheduledPriceChangeCancelled(
            ProductPriceId: streamId,
            Sku: sku.ToUpperInvariant(),
            ScheduleId: scheduleId,
            CancellationReason: "Cancelled by PricingManager",
            CancelledBy: Guid.NewGuid(), // TODO: Get from JWT claim
            CancelledAt: DateTimeOffset.UtcNow);

        session.Events.Append(streamId, evt);
        await session.SaveChangesAsync(ct);

        return Results.Ok(new
        {
            sku = sku.ToUpperInvariant(),
            scheduleId,
            message = "Scheduled price change cancelled successfully"
        });
    }
}
