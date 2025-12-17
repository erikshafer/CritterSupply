namespace Messages.Contracts.Orders;

/// <summary>
/// Integration message published by Orders BC when an order saga is successfully started.
/// Consumed by Payments and Inventory BCs to initiate their respective workflows.
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
