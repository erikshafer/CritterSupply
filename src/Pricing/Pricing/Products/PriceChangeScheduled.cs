namespace Pricing.Products;

/// <summary>
/// Domain event: Future price change registered.
/// Creates a Wolverine durable scheduled message (outgoing.ScheduleToLocalQueue).
/// ScheduleId correlates event with scheduled message for stale-message guard.
/// </summary>
public sealed record PriceChangeScheduled(
    Guid ProductPriceId,
    string Sku,
    Guid ScheduleId,
    Money ScheduledPrice,
    DateTimeOffset ScheduledFor,
    Guid ScheduledBy,
    DateTimeOffset ScheduledAt);
