namespace Storefront.Composition;

/// <summary>
/// Composed view for shopping cart (aggregates Shopping BC + Catalog BC)
/// </summary>
public sealed record CartView(
    Guid CartId,
    Guid CustomerId,
    IReadOnlyList<CartLineItemView> Items,
    decimal Subtotal);

/// <summary>
/// Line item enriched with product details from Catalog BC
/// </summary>
public sealed record CartLineItemView(
    string Sku,
    string ProductName,        // From Catalog BC
    string ProductImageUrl,    // From Catalog BC
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal);
