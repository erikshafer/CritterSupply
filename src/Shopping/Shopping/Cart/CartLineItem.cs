namespace Shopping.Cart;

public sealed record CartLineItem(
    string Sku,
    int Quantity,
    decimal UnitPrice);
