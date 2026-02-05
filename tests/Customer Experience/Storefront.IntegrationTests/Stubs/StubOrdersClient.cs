using Storefront.Clients;

namespace Storefront.IntegrationTests.Stubs;

/// <summary>
/// Stub implementation of IOrdersClient for testing
/// Returns predefined test data without making real HTTP calls
/// </summary>
public class StubOrdersClient : IOrdersClient
{
    private readonly Dictionary<Guid, CheckoutDto> _checkouts = new();
    private readonly List<OrderDto> _orders = new();

    public void AddCheckout(Guid checkoutId, Guid customerId, params CheckoutItemDto[] items)
    {
        _checkouts[checkoutId] = new CheckoutDto(checkoutId, customerId, items.ToList(), "Active");
    }

    public void AddOrder(OrderDto order)
    {
        _orders.Add(order);
    }

    public Task<CheckoutDto> GetCheckoutAsync(Guid checkoutId, CancellationToken ct = default)
    {
        if (_checkouts.TryGetValue(checkoutId, out var checkout))
        {
            return Task.FromResult(checkout);
        }

        throw new HttpRequestException($"Checkout {checkoutId} not found", null, System.Net.HttpStatusCode.NotFound);
    }

    public Task<PagedResult<OrderDto>> GetOrdersAsync(
        Guid customerId,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        var customerOrders = _orders
            .Where(o => o.CustomerId == customerId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        var totalCount = _orders.Count(o => o.CustomerId == customerId);

        return Task.FromResult(new PagedResult<OrderDto>(customerOrders, totalCount, page, pageSize));
    }
}
