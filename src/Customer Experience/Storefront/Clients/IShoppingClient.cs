namespace Storefront.Clients;

/// <summary>
/// HTTP client for querying Shopping BC
/// </summary>
public interface IShoppingClient
{
    Task<CartDto> GetCartAsync(Guid cartId, CancellationToken ct = default);
}

/// <summary>
/// Cart DTO from Shopping BC
/// </summary>
public sealed record CartDto(
    Guid Id,
    Guid CustomerId,
    IReadOnlyList<CartItemDto> Items);

/// <summary>
/// Cart item DTO from Shopping BC
/// </summary>
public sealed record CartItemDto(
    string Sku,
    int Quantity,
    decimal UnitPrice);
