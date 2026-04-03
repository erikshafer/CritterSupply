using Marten;
using Messages.Contracts.Marketplaces;
using Wolverine;
using IntegrationMessages = Messages.Contracts.Listings;

namespace Listings.Listing;

/// <summary>
/// Consumes <see cref="MarketplaceSubmissionRejected"/> from the Marketplaces BC.
/// Transitions the Listing aggregate from Submitted to Ended with
/// <see cref="EndedCause.SubmissionRejected"/>.
///
/// Idempotency: if the listing is already Ended or not found, this is a silent no-op.
/// </summary>
public static class MarketplaceSubmissionRejectedHandler
{
    public static async Task<OutgoingMessages> Handle(
        MarketplaceSubmissionRejected message,
        IDocumentSession session)
    {
        var outgoing = new OutgoingMessages();
        var now = DateTimeOffset.UtcNow;

        var listing = await session.Events.AggregateStreamAsync<Listing>(message.ListingId);

        // Idempotency: listing not found or already Ended — silent no-op
        if (listing is null || listing.Status == ListingStatus.Ended)
            return outgoing;

        // Only Submitted listings can be failed by marketplace feedback
        if (listing.Status != ListingStatus.Submitted)
            return outgoing;

        var @event = new ListingEnded(
            message.ListingId,
            message.Sku,
            message.ChannelCode,
            EndedCause.SubmissionRejected,
            now);

        session.Events.Append(message.ListingId, @event);

        outgoing.Add(new IntegrationMessages.ListingEnded(
            message.ListingId,
            message.Sku,
            message.ChannelCode,
            EndedCause.SubmissionRejected.ToString(),
            now));

        return outgoing;
    }
}
