namespace Promotions.Promotion;

/// <summary>
/// Domain event: A promotion was activated and is now available for use.
/// </summary>
public sealed record PromotionActivated(
    Guid PromotionId,
    DateTimeOffset ActivatedAt);
