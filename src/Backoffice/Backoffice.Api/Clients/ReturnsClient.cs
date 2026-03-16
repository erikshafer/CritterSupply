using Backoffice.Clients;
using System.Text.Json;

namespace Backoffice.Api.Clients;

public sealed class ReturnsClient : IReturnsClient
{
    private readonly HttpClient _httpClient;

    public ReturnsClient(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient("ReturnsClient");
    }

    public async Task<IReadOnlyList<ReturnSummaryDto>> GetReturnsAsync(
        Guid? orderId = null,
        int? limit = null,
        CancellationToken ct = default)
    {
        var url = "/api/returns";
        var queryParams = new List<string>();

        if (orderId.HasValue)
            queryParams.Add($"orderId={orderId.Value}");
        if (limit.HasValue)
            queryParams.Add($"limit={limit.Value}");

        if (queryParams.Any())
            url += "?" + string.Join("&", queryParams);

        var response = await _httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync(ct);
        var list = JsonSerializer.Deserialize<List<ReturnSummaryDto>>(content, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        return (IReadOnlyList<ReturnSummaryDto>)(list ?? new List<ReturnSummaryDto>());
    }

    public async Task<ReturnDetailDto?> GetReturnAsync(Guid returnId, CancellationToken ct = default)
    {
        var response = await _httpClient.GetAsync($"/api/returns/{returnId}", ct);

        if (!response.IsSuccessStatusCode)
            return null;

        var content = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<ReturnDetailDto>(content, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
    }

    public async Task ApproveReturnAsync(Guid returnId, CancellationToken ct = default)
    {
        var response = await _httpClient.PostAsync($"/api/returns/{returnId}/approve", null, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task DenyReturnAsync(Guid returnId, string reason, CancellationToken ct = default)
    {
        var request = new { reason };
        var response = await _httpClient.PostAsJsonAsync($"/api/returns/{returnId}/deny", request, ct);
        response.EnsureSuccessStatusCode();
    }
}
