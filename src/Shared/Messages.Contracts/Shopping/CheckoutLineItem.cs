namespace Messages.Contracts.Shopping;

public sealed record CheckoutLineItem(
    string Sku,
    int Quantity,
    decimal UnitPrice);
