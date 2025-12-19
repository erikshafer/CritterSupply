namespace Fulfillment.Shipments;

/// <summary>
/// Domain event when fulfillment is requested for an order.
/// </summary>
public sealed record FulfillmentRequested(
    Guid OrderId,
    Guid CustomerId,
    ShippingAddress ShippingAddress,
    IReadOnlyList<FulfillmentLineItem> LineItems,
    string ShippingMethod,
    DateTimeOffset RequestedAt);

public sealed record ShippingAddress(
    string AddressLine1,
    string? AddressLine2,
    string City,
    string StateProvince,
    string PostalCode,
    string Country);

public sealed record FulfillmentLineItem(
    string Sku,
    int Quantity);
