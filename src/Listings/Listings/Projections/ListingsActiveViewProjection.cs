using Listings.Listing;
using Marten.Events.Aggregation;
using Marten.Events.Projections;

namespace Listings.Projections;

/// <summary>
/// Marten document representing the active (non-ended) listing stream IDs for a given SKU.
/// Used by the recall cascade handler to find all affected listings without querying individual streams.
/// Keyed by SKU (string Id).
/// </summary>
public sealed record ListingsActiveView
{
    /// <summary>
    /// The product SKU — serves as the Marten document Id.
    /// </summary>
    public string Id { get; init; } = null!;

    /// <summary>
    /// Stream IDs of all non-ended listings for this SKU.
    /// </summary>
    public IReadOnlyList<Guid> ActiveListingStreamIds { get; init; } = [];
}

/// <summary>
/// Inline multi-stream projection maintaining ListingsActiveView.
/// Maps Listing domain events (from Guid streams) to string-keyed documents (by SKU).
/// </summary>
public sealed class ListingsActiveViewProjection : MultiStreamProjection<ListingsActiveView, string>
{
    public ListingsActiveViewProjection()
    {
        // Map each event to the SKU-keyed document
        Identity<ListingDraftCreated>(x => x.Sku);
        Identity<ListingEnded>(x => x.Sku);
        Identity<ListingForcedDown>(x => x.Sku);
    }

    public ListingsActiveView Create(ListingDraftCreated evt)
    {
        return new ListingsActiveView
        {
            Id = evt.Sku,
            ActiveListingStreamIds = [evt.ListingId]
        };
    }

    public ListingsActiveView Apply(ListingsActiveView view, ListingDraftCreated evt)
    {
        if (view.ActiveListingStreamIds.Contains(evt.ListingId))
            return view;

        return view with
        {
            ActiveListingStreamIds = [..view.ActiveListingStreamIds, evt.ListingId]
        };
    }

    public ListingsActiveView Apply(ListingsActiveView view, ListingEnded evt)
    {
        return view with
        {
            ActiveListingStreamIds = view.ActiveListingStreamIds
                .Where(id => id != evt.ListingId)
                .ToList()
        };
    }

    public ListingsActiveView Apply(ListingsActiveView view, ListingForcedDown evt)
    {
        return view with
        {
            ActiveListingStreamIds = view.ActiveListingStreamIds
                .Where(id => id != evt.ListingId)
                .ToList()
        };
    }
}
