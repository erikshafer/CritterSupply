namespace Promotions.Promotion;

/// <summary>
/// Command to create a new promotion in draft status.
/// Phase 1: Admin manually activates after creation.
/// Phase 2+: Scheduled activation via scheduled messages.
/// </summary>
public sealed record CreatePromotion(
    string Name,
    string Description,
    DiscountType DiscountType,
    decimal DiscountValue,
    DateTimeOffset StartDate,
    DateTimeOffset EndDate,
    int? UsageLimit);
