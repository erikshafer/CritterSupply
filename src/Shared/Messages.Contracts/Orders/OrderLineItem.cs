namespace Messages.Contracts.Orders;

/// <summary>
/// A line item in an order with calculated line total.
/// Part of OrderPlaced integration message.
/// </summary>
public sealed record OrderLineItem(
    string Sku,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal);
