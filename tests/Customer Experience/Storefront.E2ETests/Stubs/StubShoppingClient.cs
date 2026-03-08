using Storefront.Clients;

namespace Storefront.E2ETests.Stubs;

/// <summary>
/// Stub implementation of IShoppingClient for E2E testing.
/// Returns predefined test data without making real HTTP calls.
/// </summary>
public sealed class StubShoppingClient : IShoppingClient
{
    private readonly Dictionary<Guid, CartDto> _carts = new();
    private readonly Dictionary<Guid, Guid> _checkoutIds = new();

    public void ConfigureCart(Guid cartId, CartDto cart) => _carts[cartId] = cart;

    /// <summary>
    /// Pre-registers a deterministic checkout ID for a specific cart.
    /// Used by E2ETestFixture.SeedStandardCheckoutScenarioAsync to coordinate stubs.
    /// </summary>
    public void SetCheckoutId(Guid cartId, Guid checkoutId) => _checkoutIds[cartId] = checkoutId;

    public Task<CartDto?> GetCartAsync(Guid cartId, CancellationToken ct = default)
    {
        _carts.TryGetValue(cartId, out var cart);
        return Task.FromResult(cart);
    }

    public Task<Guid> InitializeCartAsync(Guid customerId, CancellationToken ct = default)
    {
        var cartId = Guid.CreateVersion7();
        _carts[cartId] = new CartDto(cartId, customerId, []);
        return Task.FromResult(cartId);
    }

    public Task AddItemAsync(Guid cartId, string sku, int quantity, CancellationToken ct = default)
    {
        if (!_carts.TryGetValue(cartId, out var cart))
            throw new HttpRequestException($"Cart {cartId} not found", null, System.Net.HttpStatusCode.NotFound);

        var items = cart.Items.ToList();
        var existing = items.FirstOrDefault(i => i.Sku == sku);

        // Stub uses hardcoded price (real implementation would fetch from Pricing BC)
        var stubPrice = 29.99m;

        if (existing != null)
        {
            items.Remove(existing);
            items.Add(existing with { Quantity = existing.Quantity + quantity });
        }
        else
        {
            items.Add(new CartItemDto(sku, quantity, stubPrice));
        }

        _carts[cartId] = cart with { Items = items };
        return Task.CompletedTask;
    }

    public Task RemoveItemAsync(Guid cartId, string sku, CancellationToken ct = default)
    {
        if (!_carts.TryGetValue(cartId, out var cart))
            throw new HttpRequestException($"Cart {cartId} not found", null, System.Net.HttpStatusCode.NotFound);

        _carts[cartId] = cart with { Items = cart.Items.Where(i => i.Sku != sku).ToList() };
        return Task.CompletedTask;
    }

    public Task ChangeQuantityAsync(Guid cartId, string sku, int newQuantity, CancellationToken ct = default)
    {
        if (!_carts.TryGetValue(cartId, out var cart))
            throw new HttpRequestException($"Cart {cartId} not found", null, System.Net.HttpStatusCode.NotFound);

        var items = cart.Items.ToList();
        var existing = items.FirstOrDefault(i => i.Sku == sku);
        if (existing != null)
        {
            items.Remove(existing);
            items.Add(existing with { Quantity = newQuantity });
            _carts[cartId] = cart with { Items = items };
        }

        return Task.CompletedTask;
    }

    public Task ClearCartAsync(Guid cartId, string? reason = null, CancellationToken ct = default)
    {
        if (_carts.TryGetValue(cartId, out var cart))
            _carts[cartId] = cart with { Items = [] };
        return Task.CompletedTask;
    }

    public Task<Guid> InitiateCheckoutAsync(Guid cartId, CancellationToken ct = default)
    {
        if (!_carts.TryGetValue(cartId, out var cart))
            throw new HttpRequestException($"Cart {cartId} not found", null, System.Net.HttpStatusCode.NotFound);
        if (!cart.Items.Any())
            throw new HttpRequestException("Cannot checkout an empty cart", null, System.Net.HttpStatusCode.BadRequest);

        // Return the pre-registered deterministic ID if one was set (for E2E stub coordination),
        // otherwise generate a random one.
        var checkoutId = _checkoutIds.TryGetValue(cartId, out var id) ? id : Guid.CreateVersion7();
        return Task.FromResult(checkoutId);
    }

    public void Clear()
    {
        _carts.Clear();
        _checkoutIds.Clear();
    }
}
