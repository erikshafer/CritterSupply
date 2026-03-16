namespace Backoffice.Composition;

/// <summary>
/// Composed view for CS agents: order detail with saga state
/// </summary>
public sealed record OrderDetailView(
    Guid OrderId,
    Guid CustomerId,
    string CustomerEmail,
    DateTime PlacedAt,
    string Status,
    decimal TotalAmount,
    IReadOnlyList<OrderLineItemView> Items,
    string? CancellationReason,
    bool IsReturnable,
    IReadOnlyList<ReturnableItemView> ReturnableItems);

/// <summary>
/// Order line item view for CS agents
/// </summary>
public sealed record OrderLineItemView(
    string Sku,
    string ProductName,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal);

/// <summary>
/// Returnable item view for CS agents
/// </summary>
public sealed record ReturnableItemView(
    string Sku,
    string ProductName,
    int Quantity,
    DateTime DeliveredAt,
    bool IsReturnable,
    string? IneligibilityReason);
