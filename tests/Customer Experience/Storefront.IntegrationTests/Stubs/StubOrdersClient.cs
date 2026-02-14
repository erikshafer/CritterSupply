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

    public Task ProvideShippingAddressAsync(
        Guid checkoutId,
        string addressLine1,
        string? addressLine2,
        string city,
        string stateOrProvince,
        string postalCode,
        string country,
        CancellationToken ct = default)
    {
        // Stub implementation - just verify checkout exists
        if (!_checkouts.ContainsKey(checkoutId))
            throw new HttpRequestException($"Checkout {checkoutId} not found", null, System.Net.HttpStatusCode.NotFound);

        return Task.CompletedTask;
    }

    public Task SelectShippingMethodAsync(
        Guid checkoutId,
        string shippingMethod,
        decimal shippingCost,
        CancellationToken ct = default)
    {
        // Stub implementation - just verify checkout exists
        if (!_checkouts.ContainsKey(checkoutId))
            throw new HttpRequestException($"Checkout {checkoutId} not found", null, System.Net.HttpStatusCode.NotFound);

        return Task.CompletedTask;
    }

    public Task ProvidePaymentMethodAsync(
        Guid checkoutId,
        string paymentMethodToken,
        CancellationToken ct = default)
    {
        // Stub implementation - just verify checkout exists
        if (!_checkouts.ContainsKey(checkoutId))
            throw new HttpRequestException($"Checkout {checkoutId} not found", null, System.Net.HttpStatusCode.NotFound);

        return Task.CompletedTask;
    }

    public Task<Guid> CompleteCheckoutAsync(Guid checkoutId, CancellationToken ct = default)
    {
        // Stub implementation - verify checkout exists and create order
        if (!_checkouts.TryGetValue(checkoutId, out var checkout))
            throw new HttpRequestException($"Checkout {checkoutId} not found", null, System.Net.HttpStatusCode.NotFound);

        var orderId = Guid.CreateVersion7();
        var order = new OrderDto(
            orderId,
            checkout.CustomerId,
            "Placed",
            DateTimeOffset.UtcNow,
            0m); // Stub total

        _orders.Add(order);
        _checkouts.Remove(checkoutId);

        return Task.FromResult(orderId);
    }
}
