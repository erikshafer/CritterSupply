namespace Storefront.Clients;

/// <summary>
/// HTTP client for querying and commanding Orders BC
/// </summary>
public interface IOrdersClient
{
    // Queries
    Task<CheckoutDto> GetCheckoutAsync(Guid checkoutId, CancellationToken ct = default);
    Task<PagedResult<OrderDto>> GetOrdersAsync(
        Guid customerId,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default);

    // Commands
    Task ProvideShippingAddressAsync(
        Guid checkoutId,
        string addressLine1,
        string? addressLine2,
        string city,
        string stateOrProvince,
        string postalCode,
        string country,
        CancellationToken ct = default);

    Task SelectShippingMethodAsync(
        Guid checkoutId,
        string shippingMethod,
        decimal shippingCost,
        CancellationToken ct = default);

    Task ProvidePaymentMethodAsync(
        Guid checkoutId,
        string paymentMethodToken,
        CancellationToken ct = default);

    Task<Guid> CompleteCheckoutAsync(
        Guid checkoutId,
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
