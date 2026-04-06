using Fulfillment.Shipments;

namespace Fulfillment.Routing;

/// <summary>
/// Interface for fulfillment center routing decisions.
/// Determines which FC should fulfill a given shipment based on
/// destination address, line items, and (eventually) stock availability.
/// </summary>
public interface IFulfillmentRoutingEngine
{
    Task<string> SelectFulfillmentCenterAsync(
        ShippingAddress destination,
        IReadOnlyList<FulfillmentLineItem> lineItems,
        CancellationToken ct);
}
