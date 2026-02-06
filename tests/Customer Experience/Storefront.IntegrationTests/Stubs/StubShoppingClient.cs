using Storefront.Clients;

namespace Storefront.IntegrationTests.Stubs;

/// <summary>
/// Stub implementation of IShoppingClient for testing
/// Returns predefined test data without making real HTTP calls
/// </summary>
public class StubShoppingClient : IShoppingClient
{
    private readonly Dictionary<Guid, CartDto> _carts = new();

    public void AddCart(Guid cartId, Guid customerId, params CartItemDto[] items)
    {
        _carts[cartId] = new CartDto(cartId, customerId, items.ToList());
    }

    /// <summary>
    /// Configure cart data for testing (alternative to AddCart with explicit CartDto)
    /// </summary>
    public void ConfigureCart(Guid cartId, CartDto cart)
    {
        _carts[cartId] = cart;
    }

    public Task<CartDto?> GetCartAsync(Guid cartId, CancellationToken ct = default)
    {
        if (_carts.TryGetValue(cartId, out var cart))
        {
            return Task.FromResult<CartDto?>(cart);
        }

        return Task.FromResult<CartDto?>(null);
    }

    /// <summary>
    /// Clear all configured cart data (for test isolation)
    /// </summary>
    public void Clear()
    {
        _carts.Clear();
    }
}
