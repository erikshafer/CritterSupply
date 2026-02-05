namespace Storefront.Clients;

/// <summary>
/// HTTP client for querying Orders BC
/// </summary>
public interface IOrdersClient
{
    Task<CheckoutDto> GetCheckoutAsync(Guid checkoutId, CancellationToken ct = default);
    Task<PagedResult<OrderDto>> GetOrdersAsync(
        Guid customerId,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default);
}

/// <summary>
/// Checkout DTO from Orders BC
/// </summary>
public sealed record CheckoutDto(
    Guid Id,
    Guid CustomerId,
    IReadOnlyList<CheckoutItemDto> Items,
    string Status);

/// <summary>
/// Checkout item DTO from Orders BC
/// </summary>
public sealed record CheckoutItemDto(
    string Sku,
    int Quantity,
    decimal UnitPrice);

/// <summary>
/// Order DTO from Orders BC
/// </summary>
public sealed record OrderDto(
    Guid Id,
    Guid CustomerId,
    string Status,
    DateTimeOffset PlacedAt,
    decimal Total);
