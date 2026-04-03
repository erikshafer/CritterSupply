using Marten;
using Messages.Contracts.Marketplaces;
using Wolverine;
using IntegrationMessages = Messages.Contracts.Listings;

namespace Listings.Listing;

/// <summary>
/// Consumes <see cref="MarketplaceListingActivated"/> from the Marketplaces BC.
/// Transitions the Listing aggregate from Submitted to Live.
///
/// Idempotency: if the listing is already Live or not found, this is a silent no-op.
/// Duplicate <see cref="MarketplaceListingActivated"/> messages (e.g. from both an
/// immediate Amazon/eBay path and a delayed Walmart poll) are safe to deliver more
/// than once — the status guard prevents double-append.
/// </summary>
public static class MarketplaceListingActivatedHandler
{
    public static async Task<OutgoingMessages> Handle(
        MarketplaceListingActivated message,
        IDocumentSession session)
    {
        var outgoing = new OutgoingMessages();
        var now = DateTimeOffset.UtcNow;

        var listing = await session.Events.AggregateStreamAsync<Listing>(message.ListingId);

        // Idempotency: listing not found or already Live — silent no-op
        if (listing is null || listing.Status == ListingStatus.Live)
            return outgoing;

        // Only Submitted listings can be activated by marketplace feedback
        if (listing.Status != ListingStatus.Submitted)
            return outgoing;

        var @event = new ListingActivated(message.ListingId, message.ChannelCode, now);
        session.Events.Append(message.ListingId, @event);

        outgoing.Add(new IntegrationMessages.ListingActivated(
            message.ListingId,
            message.ChannelCode,
            now));

        return outgoing;
    }
}
