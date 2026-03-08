namespace Pricing.Products;

/// <summary>
/// Domain event: Scheduled price change cancelled before activation.
/// NOTE: Does NOT cancel Wolverine scheduled message (not supported by Wolverine).
/// Stale-message guard in handler discards message when it arrives if schedule is cancelled.
/// Renamed from PriceChangeScheduleCancelled to align with ScheduledPriceActivated naming.
/// </summary>
public sealed record ScheduledPriceChangeCancelled(
    Guid ProductPriceId,
    string Sku,
    Guid ScheduleId,
    string? CancellationReason,
    Guid CancelledBy,
    DateTimeOffset CancelledAt);
