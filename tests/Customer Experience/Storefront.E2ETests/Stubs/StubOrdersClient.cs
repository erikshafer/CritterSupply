using Storefront.Clients;

namespace Storefront.E2ETests.Stubs;

/// <summary>
/// Stub implementation of IOrdersClient for E2E testing.
/// Returns predefined checkout/order data without making real HTTP calls.
/// </summary>
public sealed class StubOrdersClient : IOrdersClient
{
    private readonly Dictionary<Guid, CheckoutDto> _checkouts = new();
    private readonly List<OrderDto> _orders = new();

    public void AddCheckout(Guid checkoutId, Guid customerId, params CheckoutItemDto[] items)
        => _checkouts[checkoutId] = new CheckoutDto(checkoutId, customerId, items.ToList(), IsCompleted: false);

    public void AddOrder(OrderDto order) => _orders.Add(order);

    public Task<CheckoutDto> GetCheckoutAsync(Guid checkoutId, CancellationToken ct = default)
    {
        if (_checkouts.TryGetValue(checkoutId, out var checkout))
            return Task.FromResult(checkout);
        throw new HttpRequestException($"Checkout {checkoutId} not found", null, System.Net.HttpStatusCode.NotFound);
    }

    public Task<OrderDto?> GetOrderAsync(Guid orderId, CancellationToken ct = default)
    {
        var order = _orders.FirstOrDefault(o => o.Id == orderId);
        return Task.FromResult<OrderDto?>(order);
    }

    public Task<PagedResult<OrderDto>> GetOrdersAsync(
        Guid customerId,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        var customerOrders = _orders.Where(o => o.CustomerId == customerId).ToList();
        var items = customerOrders.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return Task.FromResult(new PagedResult<OrderDto>(items, customerOrders.Count, page, pageSize));
    }

    public Task ProvideShippingAddressAsync(Guid checkoutId, string addressLine1, string? addressLine2,
        string city, string stateOrProvince, string postalCode, string country, CancellationToken ct = default)
    {
        if (!_checkouts.ContainsKey(checkoutId))
            throw new HttpRequestException($"Checkout {checkoutId} not found", null, System.Net.HttpStatusCode.NotFound);
        return Task.CompletedTask;
    }

    public Task SelectShippingMethodAsync(Guid checkoutId, string shippingMethod, decimal shippingCost, CancellationToken ct = default)
    {
        if (!_checkouts.ContainsKey(checkoutId))
            throw new HttpRequestException($"Checkout {checkoutId} not found", null, System.Net.HttpStatusCode.NotFound);
        return Task.CompletedTask;
    }

    public Task ProvidePaymentMethodAsync(Guid checkoutId, string paymentMethodToken, CancellationToken ct = default)
    {
        if (!_checkouts.ContainsKey(checkoutId))
            throw new HttpRequestException($"Checkout {checkoutId} not found", null, System.Net.HttpStatusCode.NotFound);

        // Reject the well-known invalid test token so the "invalid payment" scenario works
        if (paymentMethodToken == WellKnownTestData.Payment.InvalidToken)
            throw new HttpRequestException("Invalid payment token", null, System.Net.HttpStatusCode.BadRequest);

        return Task.CompletedTask;
    }

    public Task<Guid> CompleteCheckoutAsync(Guid checkoutId, CancellationToken ct = default)
    {
        if (!_checkouts.TryGetValue(checkoutId, out var checkout))
            throw new HttpRequestException($"Checkout {checkoutId} not found", null, System.Net.HttpStatusCode.NotFound);

        var orderId = Guid.CreateVersion7();
        var total = checkout.Items.Sum(i => i.Quantity * i.UnitPrice);
        _orders.Add(new OrderDto(orderId, checkout.CustomerId, "Placed", DateTimeOffset.UtcNow, total));
        _checkouts.Remove(checkoutId);
        return Task.FromResult(orderId);
    }

    public void Clear()
    {
        _checkouts.Clear();
        _orders.Clear();
    }
}
