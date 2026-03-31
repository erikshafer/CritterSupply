using Backoffice.Clients;
using System.Text.Json;

namespace Backoffice.Api.Clients;

public sealed class PricingClient : IPricingClient
{
    private readonly HttpClient _httpClient;

    public PricingClient(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient("PricingClient");
    }

    public async Task<SetBasePriceResult?> SetBasePriceAsync(string sku, decimal amount, string currency = "USD", CancellationToken ct = default)
    {
        var request = new { Amount = amount, Currency = currency };
        using var response = await _httpClient.PostAsJsonAsync(
            $"/api/pricing/products/{Uri.EscapeDataString(sku)}/base-price",
            request,
            ct);

        if (!response.IsSuccessStatusCode)
            return null;

        var content = await response.Content.ReadAsStringAsync(ct);
        var result = JsonSerializer.Deserialize<SetBasePriceResponseDto>(content, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        return result is not null
            ? new SetBasePriceResult(result.Sku, result.BasePrice.Amount, result.BasePrice.Currency, result.Status, result.Message)
            : null;
    }

    public async Task<SchedulePriceChangeResult?> SchedulePriceChangeAsync(string sku, decimal newAmount, string currency, DateTimeOffset scheduledFor, CancellationToken ct = default)
    {
        var request = new { NewAmount = newAmount, Currency = currency, ScheduledFor = scheduledFor };
        using var response = await _httpClient.PostAsJsonAsync(
            $"/api/pricing/products/{Uri.EscapeDataString(sku)}/schedule",
            request,
            ct);

        if (!response.IsSuccessStatusCode)
            return null;

        var content = await response.Content.ReadAsStringAsync(ct);
        var result = JsonSerializer.Deserialize<SchedulePriceChangeResponseDto>(content, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        return result is not null
            ? new SchedulePriceChangeResult(result.Sku, result.ScheduleId, result.ScheduledPrice.Amount, result.ScheduledPrice.Currency, result.ScheduledFor, result.Message)
            : null;
    }

    public async Task<ProductPriceDto?> GetProductPriceAsync(string sku, CancellationToken ct = default)
    {
        // Note: This endpoint may not exist yet in Pricing BC - stub for now
        var response = await _httpClient.GetAsync($"/api/pricing/products/{Uri.EscapeDataString(sku)}", ct);

        if (!response.IsSuccessStatusCode)
            return null;

        var content = await response.Content.ReadAsStringAsync(ct);
        var result = JsonSerializer.Deserialize<ProductPriceResponseDto>(content, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        return result is not null
            ? new ProductPriceDto(result.Sku, result.BasePrice?.Amount, result.BasePrice?.Currency, result.Status, result.LastChangedAt)
            : null;
    }

    // Internal DTOs for deserializing Pricing BC API responses
    private sealed record SetBasePriceResponseDto(
        string Sku,
        MoneyDto BasePrice,
        string Status,
        string Message);

    private sealed record SchedulePriceChangeResponseDto(
        string Sku,
        Guid ScheduleId,
        MoneyDto ScheduledPrice,
        DateTimeOffset ScheduledFor,
        string Message);

    private sealed record ProductPriceResponseDto(
        string Sku,
        MoneyDto? BasePrice,
        string Status,
        DateTimeOffset? LastChangedAt);

    private sealed record MoneyDto(decimal Amount, string Currency);
}
