using Marketplaces.Adapters;
using Messages.Contracts.Marketplaces;
using Wolverine;

namespace Marketplaces.Api.Listings;

/// <summary>
/// Polls the Walmart feed status for a pending submission.
/// Publishes <see cref="MarketplaceListingActivated"/> on PROCESSED,
/// <see cref="MarketplaceSubmissionRejected"/> on ERROR or max-attempt timeout,
/// or reschedules with escalating delay while still pending.
/// See ADR 0055 for the full retry delay schedule and termination strategy.
/// </summary>
public static class CheckWalmartFeedStatusHandler
{
    private const int MaxAttempts = 10;

    public static async Task<OutgoingMessages> Handle(
        CheckWalmartFeedStatus message,
        IReadOnlyDictionary<string, IMarketplaceAdapter> adapters,
        IMessageBus bus)
    {
        var outgoing = new OutgoingMessages();
        var now = DateTimeOffset.UtcNow;

        if (!adapters.TryGetValue(message.ChannelCode, out var adapter))
        {
            outgoing.Add(new MarketplaceSubmissionRejected(
                message.ListingId,
                message.Sku,
                message.ChannelCode,
                $"No adapter found for channel '{message.ChannelCode}' during feed poll",
                now));
            return outgoing;
        }

        var externalSubmissionId = $"wmrt-{message.ExternalFeedId}";
        var status = await adapter.CheckSubmissionStatusAsync(externalSubmissionId);

        if (status.IsLive)
        {
            outgoing.Add(new MarketplaceListingActivated(
                message.ListingId,
                message.Sku,
                message.ChannelCode,
                externalSubmissionId,
                now));
            return outgoing;
        }

        if (status.IsFailed || message.AttemptCount >= MaxAttempts)
        {
            var reason = status.IsFailed
                ? status.FailureReason ?? "Walmart feed processing failed"
                : $"Walmart feed processing timed out after {message.AttemptCount} attempts";

            outgoing.Add(new MarketplaceSubmissionRejected(
                message.ListingId,
                message.Sku,
                message.ChannelCode,
                reason,
                now));
            return outgoing;
        }

        // Still pending — reschedule with escalating delay
        var delay = GetDelay(message.AttemptCount);
        await bus.ScheduleAsync(
            message with { AttemptCount = message.AttemptCount + 1 },
            delay);

        return outgoing;
    }

    private static TimeSpan GetDelay(int attempt) => attempt switch
    {
        1 => TimeSpan.FromMinutes(2),
        2 => TimeSpan.FromMinutes(5),
        3 => TimeSpan.FromMinutes(10),
        4 => TimeSpan.FromMinutes(20),
        _ => TimeSpan.FromMinutes(30)
    };
}
