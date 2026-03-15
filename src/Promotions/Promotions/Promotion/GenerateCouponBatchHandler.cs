using Marten;
using Wolverine;
using Wolverine.Marten;
using Promotions.Coupon;

namespace Promotions.Promotion;

public static class GenerateCouponBatchHandler
{
    /// <summary>
    /// Generates a batch of coupons using fan-out pattern.
    /// Appends CouponBatchGenerated event + returns N IssueCoupon commands via OutgoingMessages.
    /// Each IssueCoupon command creates a separate Coupon aggregate stream.
    /// Phase 1: Simple sequential code generation (PREFIX-XXXX format).
    /// </summary>
    public static async Task<OutgoingMessages> Handle(
        GenerateCouponBatch cmd,
        IDocumentSession session,
        CancellationToken ct)
    {
        // Load promotion by ID
        var promotion = await session.Events.AggregateStreamAsync<Promotion>(cmd.PromotionId, token: ct);

        if (promotion is null)
        {
            throw new InvalidOperationException(
                $"Promotion {cmd.PromotionId} not found");
        }

        // Invariant: Can only generate coupons for Draft or Active promotions
        if (promotion.Status != PromotionStatus.Draft && promotion.Status != PromotionStatus.Active)
        {
            throw new InvalidOperationException(
                $"Cannot generate coupons for promotion {promotion.Id} — " +
                $"current status is {promotion.Status}. " +
                "Coupons can only be generated for Draft or Active promotions.");
        }

        var batchId = Guid.NewGuid();
        var timestamp = DateTimeOffset.UtcNow;
        var outgoing = new OutgoingMessages();

        // Generate N IssueCoupon commands (fan-out)
        for (int i = 1; i <= cmd.Count; i++)
        {
            // Format: PREFIX-0001, PREFIX-0002, etc.
            var couponCode = $"{cmd.Prefix.ToUpperInvariant()}-{i:D4}";

            outgoing.Add(new IssueCoupon(
                couponCode,
                promotion.Id));
        }

        var batchEvent = new CouponBatchGenerated(
            promotion.Id,
            batchId,
            cmd.Prefix,
            cmd.Count,
            timestamp);

        // Manually append event to promotion stream
        session.Events.Append(cmd.PromotionId, batchEvent);

        // Return outgoing messages for fan-out
        return outgoing;
    }
}
