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

    public Task<CartDto> GetCartAsync(Guid cartId, CancellationToken ct = default)
    {
        if (_carts.TryGetValue(cartId, out var cart))
        {
            return Task.FromResult(cart);
        }

        var exception = new HttpRequestException($"Cart {cartId} not found", null, System.Net.HttpStatusCode.NotFound);
        throw exception;
    }
}
