namespace Promotions.Promotion;

/// <summary>
/// Domain event: A promotion reached its end date and expired naturally.
/// </summary>
public sealed record PromotionExpired(
    Guid PromotionId,
    DateTimeOffset ExpiredAt);
