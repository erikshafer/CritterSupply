namespace Messages.Contracts.Shopping;

/// <summary>
/// Integration message: Item added to shopping cart.
/// Published by Shopping BC for Customer Experience BC to trigger SSE push.
/// </summary>
public sealed record ItemAdded(
    Guid CartId,
    Guid CustomerId,
    string Sku,
    int Quantity,
    decimal UnitPrice,
    DateTimeOffset AddedAt);
