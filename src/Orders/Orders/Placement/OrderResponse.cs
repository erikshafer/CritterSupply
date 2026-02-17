namespace Orders.Placement;

/// <summary>
/// Response DTO for order queries, avoiding exposure of saga internals.
/// </summary>
public sealed record OrderResponse(
    Guid OrderId,
    Guid CustomerId,
    IReadOnlyList<OrderLineItem> LineItems,
    ShippingAddress ShippingAddress,
    string ShippingMethod,
    decimal TotalAmount,
    OrderStatus Status,
    DateTimeOffset PlacedAt)
{
    /// <summary>
    /// Creates an OrderResponse from an Order saga.
    /// </summary>
    public static OrderResponse From(Order saga) => new(
        saga.Id,
        saga.CustomerId,
        saga.LineItems,
        saga.ShippingAddress,
        saga.ShippingMethod,
        saga.TotalAmount,
        saga.Status,
        saga.PlacedAt);
}
