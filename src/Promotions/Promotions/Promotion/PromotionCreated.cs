namespace Promotions.Promotion;

/// <summary>
/// Domain event: A new promotion was created in draft status.
/// </summary>
public sealed record PromotionCreated(
    Guid PromotionId,
    string Name,
    string Description,
    DiscountType DiscountType,
    decimal DiscountValue,
    DateTimeOffset StartDate,
    DateTimeOffset EndDate,
    int? UsageLimit,
    DateTimeOffset CreatedAt);
