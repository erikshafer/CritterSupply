using Marten;
using Messages.Contracts.Orders;
using VendorPortal.RealTime;
using VendorPortal.VendorProductCatalog;

namespace VendorPortal.Analytics;

/// <summary>
/// Handles <see cref="OrderPlaced"/> integration messages from the Orders BC.
/// Fans out a <see cref="SalesMetricUpdated"/> SignalR push to each affected vendor tenant.
///
/// Attribution is captured at order time (current VendorProductCatalog mapping).
/// Line items with no VendorProductCatalog entry are silently skipped — this never throws,
/// to avoid blocking processing of line items for other vendors in the same order.
/// </summary>
public static class OrderPlacedAnalyticsHandler
{
    public static async Task<IEnumerable<SalesMetricUpdated>> Handle(
        OrderPlaced @event,
        IDocumentSession session,
        CancellationToken ct)
    {
        // Group line items by vendor tenant.
        // Load all SKU→Vendor mappings in one batch to minimize database round trips.
        var skus = @event.LineItems.Select(li => li.Sku).Distinct().ToArray();
        var catalogEntries = await session.LoadManyAsync<VendorProductCatalogEntry>(ct, skus);

        var entryBySku = catalogEntries
            .Where(e => e is not null && e.IsActive)
            .ToDictionary(e => e!.Sku, e => e!);

        var affectedTenants = new HashSet<Guid>();

        foreach (var lineItem in @event.LineItems)
        {
            if (entryBySku.TryGetValue(lineItem.Sku, out var entry))
            {
                affectedTenants.Add(entry.VendorTenantId);
            }
            // Unknown SKUs are silently skipped — Wolverine will trace the message at debug level.
        }

        var now = DateTimeOffset.UtcNow;

        // Fan out one lightweight "data changed, please refresh" notification per vendor tenant.
        return affectedTenants.Select(tenantId => new SalesMetricUpdated(tenantId, now));
    }
}
