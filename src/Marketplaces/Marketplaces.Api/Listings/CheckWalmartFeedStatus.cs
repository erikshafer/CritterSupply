namespace Marketplaces.Api.Listings;

/// <summary>
/// Internal scheduled message for polling Walmart feed status.
/// Not a cross-BC integration message — do not add to Messages.Contracts.
/// Scheduled per-submission via bus.ScheduleAsync() after a successful feed submission.
/// AttemptCount starts at 1 (first poll) and increments on each reschedule.
/// See ADR 0055 for retry delay schedule and max-attempt termination behaviour.
/// </summary>
public sealed record CheckWalmartFeedStatus(
    Guid ListingId,
    string Sku,
    string ChannelCode,
    string ExternalFeedId,
    int AttemptCount);
