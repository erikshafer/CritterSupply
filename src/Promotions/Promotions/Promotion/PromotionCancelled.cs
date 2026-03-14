namespace Promotions.Promotion;

/// <summary>
/// Domain event: A promotion was manually cancelled before its end date.
/// </summary>
public sealed record PromotionCancelled(
    Guid PromotionId,
    DateTimeOffset CancelledAt);
