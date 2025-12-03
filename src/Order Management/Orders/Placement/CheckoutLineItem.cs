namespace Orders.Placement;

/// <summary>
/// A line item from the checkout process, representing a product with quantity and price.
/// </summary>
public sealed record CheckoutLineItem(
    string Sku,
    int Quantity,
    decimal PriceAtPurchase);
