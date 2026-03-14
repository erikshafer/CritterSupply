namespace Promotions.Promotion;

/// <summary>
/// Command to manually activate a promotion.
/// Phase 1: Admin-triggered only.
/// Phase 2+: Could be triggered by scheduled message.
/// </summary>
public sealed record ActivatePromotion(Guid PromotionId);
