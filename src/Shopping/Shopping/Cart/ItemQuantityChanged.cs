namespace Shopping.Cart;

public sealed record ItemQuantityChanged(
    string Sku,
    int OldQuantity,
    int NewQuantity,
    DateTimeOffset ChangedAt);
