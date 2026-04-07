using Fulfillment.Shipments;

namespace Fulfillment.Routing;

// TODO: Replace after Inventory BC Remaster (Gap #2: multi-warehouse allocation).
// This stub uses simple geographic routing based on shipping address state.
// The production implementation will query Inventory for per-FC stock availability.

/// <summary>
/// Stub routing engine that selects a fulfillment center based on
/// simple geographic rules derived from the shipping address state.
/// East coast → NJ-FC, West coast → WA-FC, elsewhere → OH-FC.
/// Also supports split detection for Slice 32 (multi-FC split order routing).
/// </summary>
public sealed class StubFulfillmentRoutingEngine : IFulfillmentRoutingEngine
{
    private static readonly HashSet<string> EastCoastStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "CT", "DE", "FL", "GA", "MA", "MD", "ME", "NC", "NH", "NJ",
        "NY", "PA", "RI", "SC", "VA", "VT", "WV", "DC"
    };

    private static readonly HashSet<string> WestCoastStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "AK", "AZ", "CA", "CO", "HI", "ID", "MT", "NM", "NV", "OR", "UT", "WA", "WY"
    };

    public Task<string> SelectFulfillmentCenterAsync(
        ShippingAddress destination,
        IReadOnlyList<FulfillmentLineItem> lineItems,
        CancellationToken ct)
    {
        var fc = destination.StateProvince switch
        {
            var state when EastCoastStates.Contains(state) => "NJ-FC",
            var state when WestCoastStates.Contains(state) => "WA-FC",
            _ => "OH-FC"
        };

        return Task.FromResult(fc);
    }

    /// <summary>
    /// Stub split detection: if order has items from different "product category groups"
    /// (stub: even vs odd SKU hash), route to NJ-FC + OH-FC.
    /// Returns null if no split is needed (single FC can handle all items).
    /// </summary>
    public static IReadOnlyList<SplitProposal>? DetectSplit(
        IReadOnlyList<FulfillmentLineItem> lineItems)
    {
        if (lineItems.Count <= 1) return null;

        var evenItems = lineItems.Where(i => Math.Abs(i.Sku.GetHashCode()) % 2 == 0).ToList();
        var oddItems = lineItems.Where(i => Math.Abs(i.Sku.GetHashCode()) % 2 != 0).ToList();

        if (evenItems.Count == 0 || oddItems.Count == 0) return null;

        return new List<SplitProposal>
        {
            new("NJ-FC", evenItems),
            new("OH-FC", oddItems)
        };
    }
}
