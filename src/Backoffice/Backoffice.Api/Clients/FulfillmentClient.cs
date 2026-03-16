using Backoffice.Clients;
using System.Text.Json;

namespace Backoffice.Api.Clients;

public sealed class FulfillmentClient : IFulfillmentClient
{
    private readonly HttpClient _httpClient;

    public FulfillmentClient(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient("FulfillmentClient");
    }

    public async Task<IReadOnlyList<ShipmentDto>> GetShipmentsForOrderAsync(
        Guid orderId,
        CancellationToken ct = default)
    {
        var response = await _httpClient.GetAsync($"/api/fulfillment/shipments?orderId={orderId}", ct);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync(ct);
        var list = JsonSerializer.Deserialize<List<ShipmentDto>>(content, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        return (IReadOnlyList<ShipmentDto>)(list ?? new List<ShipmentDto>());
    }
}
