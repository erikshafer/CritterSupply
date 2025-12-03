namespace Orders.Placement;

/// <summary>
/// Domain event published when an order saga is successfully started.
/// Triggers downstream contexts (Payments, Inventory).
/// </summary>
public sealed record OrderPlaced(
    Guid OrderId,
    Guid CustomerId,
    IReadOnlyList<OrderLineItem> LineItems,
    ShippingAddress ShippingAddress,
    string ShippingMethod,
    string PaymentMethodToken,
    decimal TotalAmount,
    DateTimeOffset PlacedAt);
