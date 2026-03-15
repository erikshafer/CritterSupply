using Marten;
using Wolverine.Marten;

namespace Promotions.Promotion;

/// <summary>
/// Handler to record a redemption on the Promotion aggregate.
/// Enforces usage limit via optimistic concurrency (Marten expected version check).
/// Phase 1 (M30.0): Command defined but not yet invoked (Shopping BC integration pending).
/// Phase 2 (M30.1): OrderPlacedHandler will fan out to this command.
///
/// Concurrency Strategy:
/// Uses Marten's optimistic concurrency via [WriteAggregate] attribute.
/// If two redemptions arrive simultaneously and UsageLimit is reached,
/// Marten will throw ConcurrencyException on second commit.
/// Wolverine's retry policy handles the exception:
///   - RetryOnce
///   - Then RetryWithCooldown(100ms, 250ms)
///   - Then Discard (order proceeds but without promotion discount)
/// </summary>
public static class RecordPromotionRedemptionHandler
{
    public static PromotionRedemptionRecorded Handle(
        RecordPromotionRedemption cmd,
        [WriteAggregate] Promotion promotion)
    {
        // Invariant: Cannot record redemption on non-active promotion
        if (promotion.Status != PromotionStatus.Active)
        {
            throw new InvalidOperationException(
                $"Cannot record redemption for promotion {promotion.Id} — " +
                $"current status is {promotion.Status}. " +
                "Redemptions can only be recorded for Active promotions.");
        }

        // Invariant: Usage limit enforcement
        if (promotion.UsageLimit.HasValue &&
            promotion.CurrentRedemptionCount >= promotion.UsageLimit.Value)
        {
            throw new InvalidOperationException(
                $"Cannot record redemption for promotion {promotion.Id} — " +
                $"usage limit of {promotion.UsageLimit.Value} has been reached.");
        }

        // Record redemption (increment handled by Apply method)
        return new PromotionRedemptionRecorded(
            promotion.Id,
            cmd.OrderId,
            cmd.CustomerId,
            cmd.CouponCode,
            cmd.RedeemedAt);
    }

    /// <summary>
    /// Load method: Resolves promotion by ID.
    /// Throws if promotion doesn't exist.
    /// </summary>
    public static Task<Promotion?> Load(RecordPromotionRedemption cmd, IDocumentSession session)
    {
        return session.Events.AggregateStreamAsync<Promotion>(cmd.PromotionId);
    }
}
