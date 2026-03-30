using Listings.Listing;
using Listings.Projections;
using Marten;
using IntegrationProductContentUpdated = Messages.Contracts.ProductCatalog.ProductContentUpdated;

namespace Listings.ProductSummary;

/// <summary>
/// Consumes ProductContentUpdated integration events from Product Catalog BC.
/// Propagates content changes to all Live listings for the affected SKU.
/// Only Live listings are updated — Draft, Paused, ReadyForReview, and Submitted
/// listings will pick up content freshly when they are activated.
/// </summary>
public static class ContentPropagationHandler
{
    public static async Task Handle(
        IntegrationProductContentUpdated message,
        IDocumentSession session)
    {
        var now = DateTimeOffset.UtcNow;

        // Find all active listings for the affected SKU
        var activeView = await session.LoadAsync<ListingsActiveView>(message.Sku);
        if (activeView is null || activeView.ActiveListingStreamIds.Count == 0)
            return;

        foreach (var streamId in activeView.ActiveListingStreamIds)
        {
            // Load aggregate to check current status
            var listing = await session.Events.AggregateStreamAsync<Listing.Listing>(streamId);
            if (listing is null)
                continue;

            // Only propagate to Live listings
            if (listing.Status != ListingStatus.Live)
                continue;

            var @event = new ListingContentUpdated(
                streamId,
                message.Name,
                message.Description,
                now);

            session.Events.Append(streamId, @event);
        }
    }
}
