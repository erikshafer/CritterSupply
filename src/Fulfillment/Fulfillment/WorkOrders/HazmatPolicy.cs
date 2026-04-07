using Marten;

namespace Fulfillment.WorkOrders;

/// <summary>
/// Slice 37: Hazmat flagging policy.
/// When WorkOrderCreated is appended, this handler checks line items against
/// a stub hazmat registry. If any match, it appends HazmatItemFlagged and
/// HazmatShippingRestrictionApplied events.
/// </summary>
public static class HazmatPolicy
{
    // Stub hazmat registry — SKU prefixes that are hazmat
    private static readonly string[] HazmatPrefixes = ["FLEA-", "REPTILE-HEAT-"];

    /// <summary>
    /// Wolverine cascading handler for WorkOrderCreated.
    /// Returns events to append to the work order stream if hazmat items are detected.
    /// </summary>
    public static async Task Handle(
        WorkOrderCreated @event,
        IDocumentSession session,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var eventsToAppend = new List<object>();

        foreach (var lineItem in @event.LineItems)
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

            session.Events.Append(@event.WorkOrderId, eventsToAppend.ToArray());
        }
    }

    private static bool IsHazmat(string sku) =>
        HazmatPrefixes.Any(prefix => sku.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
}
