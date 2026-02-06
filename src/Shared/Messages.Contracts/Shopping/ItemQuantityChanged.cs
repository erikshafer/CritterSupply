namespace Messages.Contracts.Shopping;

/// <summary>
/// Integration message: Cart item quantity changed.
/// Published by Shopping BC for Customer Experience BC to trigger SSE push.
/// </summary>
public sealed record ItemQuantityChanged(
    Guid CartId,
    Guid CustomerId,
    string Sku,
    int OldQuantity,
    int NewQuantity,
    DateTimeOffset ChangedAt);
