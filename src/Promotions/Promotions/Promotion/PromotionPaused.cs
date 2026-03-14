namespace Promotions.Promotion;

/// <summary>
/// Domain event: A promotion was temporarily paused by an admin.
/// </summary>
public sealed record PromotionPaused(
    Guid PromotionId,
    DateTimeOffset PausedAt);
