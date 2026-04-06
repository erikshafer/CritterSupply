namespace Fulfillment.WorkOrders;

/// <summary>
/// Represents a line item on a work order with quantity tracking for pick/pack.
/// </summary>
public sealed record WorkOrderLineItem(
    string Sku,
    int Quantity);
