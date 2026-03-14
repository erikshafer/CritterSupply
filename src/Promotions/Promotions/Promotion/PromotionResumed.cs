namespace Promotions.Promotion;

/// <summary>
/// Domain event: A paused promotion was resumed and is active again.
/// </summary>
public sealed record PromotionResumed(
    Guid PromotionId,
    DateTimeOffset ResumedAt);
