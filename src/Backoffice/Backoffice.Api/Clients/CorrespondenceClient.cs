using Backoffice.Clients;
using System.Text.Json;

namespace Backoffice.Api.Clients;

public sealed class CorrespondenceClient : ICorrespondenceClient
{
    private readonly HttpClient _httpClient;

    public CorrespondenceClient(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient("CorrespondenceClient");
    }

    public async Task<IReadOnlyList<CorrespondenceMessageDto>> GetMessagesForCustomerAsync(
        Guid customerId,
        int? limit = null,
        CancellationToken ct = default)
    {
        var url = $"/api/correspondence/customers/{customerId}/messages";
        if (limit.HasValue)
            url += $"?limit={limit.Value}";

        var response = await _httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync(ct);
        var list = JsonSerializer.Deserialize<List<CorrespondenceMessageDto>>(content, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        return (IReadOnlyList<CorrespondenceMessageDto>)(list ?? new List<CorrespondenceMessageDto>());
    }

    public async Task<CorrespondenceDetailDto?> GetMessageDetailAsync(Guid messageId, CancellationToken ct = default)
    {
        var response = await _httpClient.GetAsync($"/api/correspondence/messages/{messageId}", ct);

        if (!response.IsSuccessStatusCode)
            return null;

        var content = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<CorrespondenceDetailDto>(content, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
    }
}
