namespace Pricing.Products;

/// <summary>
/// Value object representing a pending scheduled price change.
/// Embedded in ProductPrice aggregate state.
/// </summary>
public sealed record ScheduledPriceChange
{
    /// <summary>
    /// Correlation ID for the Wolverine scheduled message.
    /// Used to discard stale messages if schedule is cancelled before activation.
    /// </summary>
    public required Guid ScheduleId { get; init; }

    /// <summary>
    /// The price that will become active when the schedule fires.
    /// </summary>
    public required Money ScheduledPrice { get; init; }

    /// <summary>
    /// When the price change will take effect (UTC).
    /// Must be in the future when schedule is created.
    /// </summary>
    public required DateTimeOffset ScheduledFor { get; init; }

    /// <summary>
    /// Who scheduled this price change.
    /// </summary>
    public required Guid ScheduledBy { get; init; }

    /// <summary>
    /// When the schedule was created (UTC).
    /// </summary>
    public required DateTimeOffset ScheduledAt { get; init; }
}
