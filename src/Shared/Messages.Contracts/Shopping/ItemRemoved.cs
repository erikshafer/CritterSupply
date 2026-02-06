namespace Messages.Contracts.Shopping;

/// <summary>
/// Integration message: Item removed from shopping cart.
/// Published by Shopping BC for Customer Experience BC to trigger SSE push.
/// </summary>
public sealed record ItemRemoved(
    Guid CartId,
    Guid CustomerId,
    string Sku,
    DateTimeOffset RemovedAt);
