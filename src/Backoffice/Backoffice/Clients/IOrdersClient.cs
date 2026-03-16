namespace Backoffice.Clients;

/// <summary>
/// HTTP client for querying Orders BC (admin use)
/// </summary>
public interface IOrdersClient
{
    /// <summary>
    /// List orders for a customer (CS workflow: order history lookup)
    /// </summary>
    Task<IReadOnlyList<OrderSummaryDto>> GetOrdersAsync(
        Guid customerId,
        int? limit = null,
        CancellationToken ct = default);

    /// <summary>
    /// Get detailed order information (CS workflow: order detail view)
    /// </summary>
    Task<OrderDetailDto?> GetOrderAsync(Guid orderId, CancellationToken ct = default);

    /// <summary>
    /// Cancel an order (CS workflow: order cancellation)
    /// </summary>
    Task CancelOrderAsync(Guid orderId, CancellationToken ct = default);

    /// <summary>
    /// Get returnable items for an order (CS workflow: return eligibility check)
    /// </summary>
    Task<IReadOnlyList<ReturnableItemDto>> GetReturnableItemsAsync(
        Guid orderId,
        CancellationToken ct = default);
}

/// <summary>
/// Order summary DTO from Orders BC
/// </summary>
public sealed record OrderSummaryDto(
    Guid Id,
    Guid CustomerId,
    DateTime PlacedAt,
    string Status,
    decimal TotalAmount);

/// <summary>
/// Order detail DTO from Orders BC
/// </summary>
public sealed record OrderDetailDto(
    Guid Id,
    Guid CustomerId,
    DateTime PlacedAt,
    string Status,
    decimal TotalAmount,
    IReadOnlyList<OrderLineItemDto> Items,
    string? CancellationReason);

/// <summary>
/// Order line item DTO
/// </summary>
public sealed record OrderLineItemDto(
    string Sku,
    string ProductName,
    int Quantity,
    decimal UnitPrice);

/// <summary>
/// Returnable item DTO from Orders BC
/// </summary>
public sealed record ReturnableItemDto(
    string Sku,
    string ProductName,
    int Quantity,
    DateTime DeliveredAt,
    bool IsReturnable,
    string? IneligibilityReason);
