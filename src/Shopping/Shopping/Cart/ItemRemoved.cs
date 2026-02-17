namespace Shopping.Cart;

public sealed record ItemRemoved(
    string Sku,
    DateTimeOffset RemovedAt);
