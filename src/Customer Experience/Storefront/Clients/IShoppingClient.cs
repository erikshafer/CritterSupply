namespace Storefront.Clients;

/// <summary>
/// HTTP client for querying and commanding Shopping BC
/// </summary>
public interface IShoppingClient
{
    // Query
    Task<CartDto?> GetCartAsync(Guid cartId, CancellationToken ct = default);

    // Commands
    Task<Guid> InitializeCartAsync(Guid customerId, CancellationToken ct = default);
    Task AddItemAsync(Guid cartId, string sku, int quantity, decimal unitPrice, CancellationToken ct = default);
    Task RemoveItemAsync(Guid cartId, string sku, CancellationToken ct = default);
    Task ChangeQuantityAsync(Guid cartId, string sku, int newQuantity, CancellationToken ct = default);
    Task ClearCartAsync(Guid cartId, string? reason = null, CancellationToken ct = default);
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
