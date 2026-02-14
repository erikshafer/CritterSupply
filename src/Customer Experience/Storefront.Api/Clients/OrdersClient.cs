using Storefront.Clients;
using System.Net.Http.Json;
using System.Text.Json;

namespace Storefront.Api.Clients;

public sealed class OrdersClient : IOrdersClient
{
    private readonly HttpClient _httpClient;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public OrdersClient(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient("OrdersClient");
    }

    public async Task<CheckoutDto> GetCheckoutAsync(Guid checkoutId, CancellationToken ct = default)
    {
        var response = await _httpClient.GetAsync($"/api/checkouts/{checkoutId}", ct);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<CheckoutDto>(content, JsonOptions)
               ?? throw new InvalidOperationException($"Checkout {checkoutId} not found");
    }

    public async Task<PagedResult<OrderDto>> GetOrdersAsync(
        Guid customerId,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        var url = $"/api/orders?customerId={customerId}&page={page}&pageSize={pageSize}";

        var response = await _httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<PagedResult<OrderDto>>(content, JsonOptions)
               ?? new PagedResult<OrderDto>([], 0, page, pageSize);
    }

    public async Task ProvideShippingAddressAsync(
        Guid checkoutId,
        string addressLine1,
        string? addressLine2,
        string city,
        string stateOrProvince,
        string postalCode,
        string country,
        CancellationToken ct = default)
    {
        var payload = new
        {
            checkoutId,
            addressLine1,
            addressLine2,
            city,
            stateOrProvince,
            postalCode,
            country
        };

        var response = await _httpClient.PostAsJsonAsync($"/api/checkouts/{checkoutId}/shipping-address", payload, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task SelectShippingMethodAsync(
        Guid checkoutId,
        string shippingMethod,
        decimal shippingCost,
        CancellationToken ct = default)
    {
        var payload = new
        {
            checkoutId,
            shippingMethod,
            shippingCost
        };

        var response = await _httpClient.PostAsJsonAsync($"/api/checkouts/{checkoutId}/shipping-method", payload, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task ProvidePaymentMethodAsync(
        Guid checkoutId,
        string paymentMethodToken,
        CancellationToken ct = default)
    {
        var payload = new
        {
            checkoutId,
            paymentMethodToken
        };

        var response = await _httpClient.PostAsJsonAsync($"/api/checkouts/{checkoutId}/payment-method", payload, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<Guid> CompleteCheckoutAsync(Guid checkoutId, CancellationToken ct = default)
    {
        var payload = new { checkoutId };

        var response = await _httpClient.PostAsJsonAsync($"/api/checkouts/{checkoutId}/complete", payload, ct);
        response.EnsureSuccessStatusCode();

        // The CompleteCheckout handler returns a CheckoutCompleted event with OrderId
        // For now, we'll return the checkoutId (in real implementation, parse response for orderId)
        // TODO: Parse response to extract OrderId from CheckoutCompleted event
        return checkoutId;
    }
}
