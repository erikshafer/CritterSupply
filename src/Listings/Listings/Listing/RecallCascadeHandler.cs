using Listings.Listing;
using Listings.Projections;
using Marten;
using Wolverine;
using IntegrationProductDiscontinued = Messages.Contracts.ProductCatalog.ProductDiscontinued;
using IntegrationMessages = Messages.Contracts.Listings;

namespace Listings.Listing;

/// <summary>
/// Consumes ProductDiscontinued messages from the listings-product-recall queue.
/// When IsRecall is true, forces down all active listings for the affected SKU.
/// Idempotent — skips listings already in terminal state.
/// </summary>
public static class RecallCascadeHandler
{
    public static async Task<OutgoingMessages> Handle(
        IntegrationProductDiscontinued message,
        IDocumentSession session)
    {
        var outgoing = new OutgoingMessages();
        var now = DateTimeOffset.UtcNow;

        // Only process recall-flagged discontinuations
        if (!message.IsRecall)
            return outgoing;

        // Find all active listings for this SKU
        var activeView = await session.LoadAsync<ListingsActiveView>(message.Sku);
        if (activeView is null || activeView.ActiveListingStreamIds.Count == 0)
        {
            // No active listings — publish cascade completed with zero affected
            outgoing.Add(new IntegrationMessages.ListingsCascadeCompleted(
                message.Sku,
                0,
                now));
            return outgoing;
        }

        var affectedCount = 0;
        var recallReason = message.Reason ?? "Product recall";

        foreach (var streamId in activeView.ActiveListingStreamIds)
        {
            // Load current aggregate state for idempotency check
            var listing = await session.Events.AggregateStreamAsync<Listing>(streamId);
            if (listing is null || listing.IsTerminal)
                continue;

            var forcedDown = new ListingForcedDown(
                streamId,
                message.Sku,
                listing.ChannelCode,
                recallReason,
                now);

            session.Events.Append(streamId, forcedDown);

            outgoing.Add(new IntegrationMessages.ListingForcedDown(
                streamId,
                message.Sku,
                listing.ChannelCode,
                recallReason,
                now));

            affectedCount++;
        }

        outgoing.Add(new IntegrationMessages.ListingsCascadeCompleted(
            message.Sku,
            affectedCount,
            now));

        return outgoing;
    }
}
