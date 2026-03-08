namespace Pricing.Products;

/// <summary>
/// Domain event: Scheduled price change fired and activated.
/// Triggered by Wolverine scheduled message arrival.
/// Separate from PriceChanged — system-driven (not user-driven), queryable separately.
/// ScheduleId must match PendingSchedule.ScheduleId or handler discards (stale-message guard).
/// </summary>
public sealed record ScheduledPriceActivated(
    Guid ProductPriceId,
    string Sku,
    Guid ScheduleId,
    Money ActivatedPrice,
    DateTimeOffset ActivatedAt);
