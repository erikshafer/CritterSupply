using Wolverine.Marten;

namespace Promotions.Promotion;

public static class ActivatePromotionHandler
{
    public static async Task<PromotionActivated> Handle(
        ActivatePromotion command,
        [WriteAggregate] Promotions.Promotion.Promotion promotion,
        CancellationToken ct)
    {
        // Business rule: can only activate promotions in Draft or Paused status
        if (promotion.Status != PromotionStatus.Draft && promotion.Status != PromotionStatus.Paused)
        {
            throw new InvalidOperationException(
                $"Cannot activate promotion in {promotion.Status} status. " +
                $"Promotion must be in Draft or Paused status.");
        }

        var now = DateTimeOffset.UtcNow;

        return new PromotionActivated(
            PromotionId: command.PromotionId,
            ActivatedAt: now);
    }
}
