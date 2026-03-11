using Messages.Contracts.Common;

namespace Messages.Contracts.Fulfillment;

/// <summary>
/// Integration message from Orders to Fulfillment.
/// Orders publishes this after both payment is confirmed and inventory is committed.
/// </summary>
public sealed record FulfillmentRequested(
    Guid OrderId,
    Guid CustomerId,
    SharedShippingAddress ShippingAddress,
    IReadOnlyList<FulfillmentLineItem> LineItems,
    string ShippingMethod,
    DateTimeOffset RequestedAt);

public sealed record FulfillmentLineItem(
    string Sku,
    int Quantity);
