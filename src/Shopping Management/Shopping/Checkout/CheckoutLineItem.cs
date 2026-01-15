namespace Shopping.Checkout;

public sealed record CheckoutLineItem(
    string Sku,
    int Quantity,
    decimal UnitPrice);
