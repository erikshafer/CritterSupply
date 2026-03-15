using Shopping.Clients;
using System.Net.Http.Json;
using System.Text.Json;

namespace Shopping.Api.Clients;

public sealed class PromotionsClient : IPromotionsClient
{
    private readonly HttpClient _httpClient;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public PromotionsClient(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient("PromotionsClient");
    }

    public async Task<CouponValidationDto> ValidateCouponAsync(string couponCode, CancellationToken ct = default)
    {
        // Normalize coupon code (Promotions BC uses uppercase)
        var normalizedCode = couponCode.ToUpperInvariant();

        var response = await _httpClient.GetAsync($"/api/promotions/coupons/{normalizedCode}/validate", ct);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return new CouponValidationDto(
                IsValid: false,
                CouponCode: normalizedCode,
                Reason: "Coupon not found");
        }

        response.EnsureSuccessStatusCode();

        var validation = await response.Content.ReadFromJsonAsync<PromotionsValidationResponse>(JsonOptions, ct);

        if (validation is null)
        {
            return new CouponValidationDto(
                IsValid: false,
                CouponCode: normalizedCode,
                Reason: "Invalid response from Promotions BC");
        }

        return new CouponValidationDto(
            validation.IsValid,
            validation.CouponCode ?? normalizedCode,
            validation.PromotionId,
            validation.PromotionName,
            validation.DiscountType,
            validation.DiscountValue,
            validation.Reason);
    }

    public async Task<DiscountCalculationDto> CalculateDiscountAsync(
        IReadOnlyList<CartItemDto> cartItems,
        IReadOnlyList<string> couponCodes,
        CancellationToken ct = default)
    {
        var request = new CalculateDiscountRequest(
            cartItems.Select(i => new PromotionsCartItem(i.Sku, i.Quantity, i.UnitPrice)).ToList(),
            couponCodes.ToList());

        var response = await _httpClient.PostAsJsonAsync("/api/promotions/discounts/calculate", request, JsonOptions, ct);
        response.EnsureSuccessStatusCode();

        var calculation = await response.Content.ReadFromJsonAsync<PromotionsDiscountResponse>(JsonOptions, ct);

        if (calculation is null)
        {
            throw new InvalidOperationException("Invalid response from Promotions BC");
        }

        return new DiscountCalculationDto(
            calculation.LineItemDiscounts.Select(d => new LineItemDiscountDto(
                d.Sku,
                d.OriginalPrice,
                d.DiscountedPrice,
                d.DiscountAmount,
                d.AppliedCouponCode)).ToList(),
            calculation.TotalDiscount,
            calculation.OriginalTotal,
            calculation.DiscountedTotal);
    }

    // Response types matching Promotions BC API
    private sealed record PromotionsValidationResponse(
        bool IsValid,
        string? CouponCode,
        Guid? PromotionId,
        string? PromotionName,
        string? DiscountType,
        decimal? DiscountValue,
        string? Reason);

    private sealed record CalculateDiscountRequest(
        List<PromotionsCartItem> CartItems,
        List<string> CouponCodes);

    private sealed record PromotionsCartItem(
        string Sku,
        int Quantity,
        decimal UnitPrice);

    private sealed record PromotionsDiscountResponse(
        List<PromotionsLineItemDiscount> LineItemDiscounts,
        decimal TotalDiscount,
        decimal OriginalTotal,
        decimal DiscountedTotal);

    private sealed record PromotionsLineItemDiscount(
        string Sku,
        decimal OriginalPrice,
        decimal DiscountedPrice,
        decimal DiscountAmount,
        string? AppliedCouponCode);
}
