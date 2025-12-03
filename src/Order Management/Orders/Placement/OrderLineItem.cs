namespace Orders.Placement;

/// <summary>
/// A line item in an order with calculated line total.
/// </summary>
public sealed record OrderLineItem(
    string Sku,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal);
