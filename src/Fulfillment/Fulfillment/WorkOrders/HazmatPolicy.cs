using Marten;

namespace Fulfillment.WorkOrders;

/// <summary>
/// Slice 37: Hazmat flagging policy.
/// Checks work order line items against a stub hazmat registry.
/// If any match, appends HazmatItemFlagged and HazmatShippingRestrictionApplied events.
/// Called inline after WorkOrderCreated is appended.
/// </summary>
public static class HazmatPolicy
{
    // Stub hazmat registry — SKU prefixes that are hazmat
    private static readonly string[] HazmatPrefixes = ["FLEA-", "REPTILE-HEAT-"];

    /// <summary>
    /// Checks line items against the hazmat registry and appends flagging events
    /// to the work order stream if any hazmat items are detected.
    /// </summary>
    public static void CheckAndApply(
        Guid workOrderId,
        IReadOnlyList<WorkOrderLineItem> lineItems,
        IDocumentSession session)
    {
        var now = DateTimeOffset.UtcNow;
        var eventsToAppend = new List<object>();

        foreach (var lineItem in lineItems)
        {
            if (IsHazmat(lineItem.Sku))
            {
                var hazmatClass = lineItem.Sku.StartsWith("FLEA-", StringComparison.OrdinalIgnoreCase)
                    ? "ORM-D" // Other Regulated Material — Domestic
                    : "Class 9"; // Miscellaneous hazardous

                eventsToAppend.Add(new HazmatItemFlagged(lineItem.Sku, hazmatClass, now));
            }
        }

        if (eventsToAppend.Count > 0)
        {
            // Downgrade: air shipping blocked, ground only
            eventsToAppend.Add(new HazmatShippingRestrictionApplied("AirShipping", now));

            session.Events.Append(workOrderId, eventsToAppend.ToArray());
        }
    }

    private static bool IsHazmat(string sku) =>
        HazmatPrefixes.Any(prefix => sku.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
}
