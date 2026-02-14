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

    public Task<Guid> InitializeCartAsync(Guid customerId, CancellationToken ct = default)
    {
        var cartId = Guid.CreateVersion7();
        _carts[cartId] = new CartDto(cartId, customerId, []);
        return Task.FromResult(cartId);
    }

    public Task AddItemAsync(Guid cartId, string sku, int quantity, decimal unitPrice, CancellationToken ct = default)
    {
        if (_carts.TryGetValue(cartId, out var cart))
        {
            var items = cart.Items.ToList();
            var existingItem = items.FirstOrDefault(i => i.Sku == sku);

            if (existingItem != null)
            {
                items.Remove(existingItem);
                items.Add(existingItem with { Quantity = existingItem.Quantity + quantity });
            }
            else
            {
                items.Add(new CartItemDto(sku, quantity, unitPrice));
            }

            _carts[cartId] = cart with { Items = items };
        }

        return Task.CompletedTask;
    }

    public Task RemoveItemAsync(Guid cartId, string sku, CancellationToken ct = default)
    {
        if (_carts.TryGetValue(cartId, out var cart))
        {
            var items = cart.Items.Where(i => i.Sku != sku).ToList();
            _carts[cartId] = cart with { Items = items };
        }

        return Task.CompletedTask;
    }

    public Task ChangeQuantityAsync(Guid cartId, string sku, int newQuantity, CancellationToken ct = default)
    {
        if (_carts.TryGetValue(cartId, out var cart))
        {
            var items = cart.Items.ToList();
            var existingItem = items.FirstOrDefault(i => i.Sku == sku);

            if (existingItem != null)
            {
                items.Remove(existingItem);
                items.Add(existingItem with { Quantity = newQuantity });
                _carts[cartId] = cart with { Items = items };
            }
        }

        return Task.CompletedTask;
    }

    public Task ClearCartAsync(Guid cartId, string? reason = null, CancellationToken ct = default)
    {
        if (_carts.ContainsKey(cartId))
        {
            var cart = _carts[cartId];
            _carts[cartId] = cart with { Items = [] };
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Clear all configured cart data (for test isolation)
    /// </summary>
    public void Clear()
    {
        _carts.Clear();
    }
}
