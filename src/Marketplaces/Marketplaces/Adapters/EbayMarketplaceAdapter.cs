using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Marketplaces.Credentials;
using Microsoft.Extensions.Logging;

namespace Marketplaces.Adapters;

/// <summary>
/// Production eBay Sell API adapter that submits listings via the Inventory API (Offer endpoints).
/// <para>
/// Authentication uses OAuth 2.0 refresh token grant:
/// <list type="number">
/// <item>Exchange client credentials (HTTP Basic) + refresh token for an access token</item>
/// <item>Use the access token as <c>Authorization: Bearer</c> header on API calls</item>
/// </list>
/// See ADR 0054 for the complete authentication flow documentation.
/// </para>
/// <para>
/// Listing submission is a two-step process:
/// <list type="number">
/// <item>Create offer: <c>POST /sell/inventory/v1/offer</c></item>
/// <item>Publish offer: <c>POST /sell/inventory/v1/offer/{offerId}/publish</c></item>
/// </list>
/// </para>
/// <para>
/// Credentials are retrieved from <see cref="IVaultClient"/> using vault path conventions
/// defined in ADR 0051 (e.g., <c>ebay/client-id</c>, <c>ebay/client-secret</c>).
/// </para>
/// </summary>
public sealed class EbayMarketplaceAdapter : IMarketplaceAdapter
{
    private readonly IVaultClient _vault;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<EbayMarketplaceAdapter> _logger;

    // OAuth token cache — avoids repeated vault reads and token exchanges per request
    private string? _cachedAccessToken;
    private DateTimeOffset _tokenExpiry = DateTimeOffset.MinValue;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    // eBay API endpoints
    private const string TokenUrl = "https://api.ebay.com/identity/v1/oauth2/token";
    private const string OfferBaseUrl = "https://api.ebay.com/sell/inventory/v1/offer";
    private const string SellInventoryScope = "https://api.ebay.com/oauth/api_scope/sell.inventory";

    public string ChannelCode => "EBAY_US";

    public EbayMarketplaceAdapter(
        IVaultClient vault,
        IHttpClientFactory httpClientFactory,
        ILogger<EbayMarketplaceAdapter> logger)
    {
        _vault = vault;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<SubmissionResult> SubmitListingAsync(
        ListingSubmission submission,
        CancellationToken ct = default)
    {
        try
        {
            var accessToken = await GetAccessTokenAsync(ct);
            var marketplaceId = await _vault.GetSecretAsync("ebay/marketplace-id", ct);

            var client = _httpClientFactory.CreateClient("EbayApi");

            // Step 1: Create offer — POST /sell/inventory/v1/offer
            var offerBody = BuildCreateOfferBody(submission, marketplaceId);

            using var createRequest = new HttpRequestMessage(HttpMethod.Post, OfferBaseUrl);
            createRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            createRequest.Headers.Add("X-EBAY-C-MARKETPLACE-ID", marketplaceId);
            createRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            createRequest.Content = JsonContent.Create(offerBody, options: JsonOptions);

            _logger.LogInformation(
                "Creating eBay offer: SKU={Sku}, MarketplaceId={MarketplaceId}",
                submission.Sku, marketplaceId);

            using var createResponse = await client.SendAsync(createRequest, ct);

            if (!createResponse.IsSuccessStatusCode)
            {
                var errorBody = await createResponse.Content.ReadAsStringAsync(ct);
                _logger.LogWarning(
                    "eBay create offer failed: SKU={Sku}, StatusCode={StatusCode}, Body={Body}",
                    submission.Sku, (int)createResponse.StatusCode, errorBody);

                return new SubmissionResult(
                    IsSuccess: false,
                    ExternalSubmissionId: null,
                    ErrorMessage: $"eBay create offer returned {(int)createResponse.StatusCode}: {errorBody}");
            }

            var createResult = await createResponse.Content.ReadFromJsonAsync<EbayCreateOfferResponse>(JsonOptions, ct);
            var offerId = createResult?.OfferId
                ?? throw new InvalidOperationException("eBay create offer response missing offerId");

            _logger.LogInformation(
                "eBay offer created: SKU={Sku}, OfferId={OfferId}. Publishing...",
                submission.Sku, offerId);

            // Step 2: Publish offer — POST /sell/inventory/v1/offer/{offerId}/publish
            var publishUrl = $"{OfferBaseUrl}/{offerId}/publish";

            using var publishRequest = new HttpRequestMessage(HttpMethod.Post, publishUrl);
            publishRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            publishRequest.Headers.Add("X-EBAY-C-MARKETPLACE-ID", marketplaceId);
            publishRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var publishResponse = await client.SendAsync(publishRequest, ct);

            if (publishResponse.IsSuccessStatusCode)
            {
                _logger.LogInformation(
                    "eBay offer published successfully: SKU={Sku}, OfferId={OfferId}",
                    submission.Sku, offerId);

                return new SubmissionResult(
                    IsSuccess: true,
                    ExternalSubmissionId: $"ebay-{offerId}");
            }

            var publishError = await publishResponse.Content.ReadAsStringAsync(ct);
            _logger.LogWarning(
                "eBay publish offer failed: SKU={Sku}, OfferId={OfferId}, StatusCode={StatusCode}, Body={Body}",
                submission.Sku, offerId, (int)publishResponse.StatusCode, publishError);

            return new SubmissionResult(
                IsSuccess: false,
                ExternalSubmissionId: null,
                ErrorMessage: $"eBay publish offer returned {(int)publishResponse.StatusCode}: {publishError}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to submit listing to eBay Sell API: SKU={Sku}", submission.Sku);
            return new SubmissionResult(
                IsSuccess: false,
                ExternalSubmissionId: null,
                ErrorMessage: $"eBay adapter error: {ex.Message}");
        }
    }

    public Task<SubmissionStatus> CheckSubmissionStatusAsync(
        string externalSubmissionId,
        CancellationToken ct = default)
    {
        // Skeleton implementation — real status checking deferred to M38.x (D-3).
        // A full implementation would call GET /sell/inventory/v1/offer/{offerId}
        // to check the offer status and listing details.
        _logger.LogInformation(
            "CheckSubmissionStatusAsync not yet implemented for eBay Sell API. " +
            "Returning pending status for submission {SubmissionId}.",
            externalSubmissionId);

        return Task.FromResult(new SubmissionStatus(
            ExternalSubmissionId: externalSubmissionId,
            IsLive: false,
            IsFailed: false,
            FailureReason: "Status polling not yet implemented — deferred to M38.x"));
    }

    public Task<bool> DeactivateListingAsync(
        string externalListingId,
        CancellationToken ct = default)
    {
        // Skeleton implementation — eBay offer withdrawal uses:
        // POST /sell/inventory/v1/offer/{offerId}/withdraw
        // Full implementation deferred to a future session.
        _logger.LogInformation(
            "DeactivateListingAsync not yet implemented for eBay Sell API. " +
            "Returning false for listing {ListingId}.",
            externalListingId);

        return Task.FromResult(false);
    }

    /// <summary>
    /// Obtains an OAuth access token via refresh token grant, using cached token if still valid.
    /// eBay tokens typically have a 2-hour TTL; we refresh 5 minutes before expiry.
    /// </summary>
    private async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        if (_cachedAccessToken is not null && DateTimeOffset.UtcNow < _tokenExpiry)
            return _cachedAccessToken;

        await _tokenLock.WaitAsync(ct);
        try
        {
            // Double-check after acquiring lock
            if (_cachedAccessToken is not null && DateTimeOffset.UtcNow < _tokenExpiry)
                return _cachedAccessToken;

            var clientId = await _vault.GetSecretAsync("ebay/client-id", ct);
            var clientSecret = await _vault.GetSecretAsync("ebay/client-secret", ct);
            var refreshToken = await _vault.GetSecretAsync("ebay/refresh-token", ct);

            var client = _httpClientFactory.CreateClient("EbayApi");

            // eBay OAuth uses HTTP Basic auth: Base64(client_id:client_secret)
            // UTF-8 encoding is required per RFC 7617 to handle special characters safely
            var credentials = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));

            var tokenRequest = new HttpRequestMessage(HttpMethod.Post, TokenUrl);
            tokenRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            tokenRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            tokenRequest.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["scope"] = SellInventoryScope
            });

            using var response = await client.SendAsync(tokenRequest, ct);
            response.EnsureSuccessStatusCode();

            var tokenResponse = await response.Content.ReadFromJsonAsync<EbayTokenResponse>(JsonOptions, ct);

            _cachedAccessToken = tokenResponse?.AccessToken
                ?? throw new InvalidOperationException("eBay token response missing access_token");

            // Cache with 5-minute safety margin before actual expiry
            var expiresInSeconds = tokenResponse.ExpiresIn > 0 ? tokenResponse.ExpiresIn : 7200;
            _tokenExpiry = DateTimeOffset.UtcNow.AddSeconds(expiresInSeconds - 300);

            _logger.LogInformation(
                "eBay OAuth access token refreshed successfully. Expires at {Expiry}.",
                _tokenExpiry);

            return _cachedAccessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    /// <summary>
    /// Builds the eBay create-offer request body.
    /// </summary>
    private static EbayCreateOfferRequest BuildCreateOfferBody(
        ListingSubmission submission,
        string marketplaceId)
    {
        return new EbayCreateOfferRequest
        {
            Sku = submission.Sku,
            MarketplaceId = marketplaceId,
            Format = "FIXED_PRICE",
            ListingDescription = submission.Description,
            PricingSummary = new EbayPricingSummary
            {
                Price = new EbayAmount
                {
                    Value = submission.Price.ToString("F2"),
                    Currency = "USD"
                }
            },
            CategoryId = submission.Category
        };
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    // --- eBay API DTOs (internal to adapter) ---

    private sealed record EbayTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; init; }

        [JsonPropertyName("token_type")]
        public string? TokenType { get; init; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; init; }
    }

    private sealed record EbayCreateOfferRequest
    {
        [JsonPropertyName("sku")]
        public string Sku { get; init; } = string.Empty;

        [JsonPropertyName("marketplaceId")]
        public string MarketplaceId { get; init; } = string.Empty;

        [JsonPropertyName("format")]
        public string Format { get; init; } = "FIXED_PRICE";

        [JsonPropertyName("listingDescription")]
        public string? ListingDescription { get; init; }

        [JsonPropertyName("pricingSummary")]
        public EbayPricingSummary? PricingSummary { get; init; }

        [JsonPropertyName("categoryId")]
        public string? CategoryId { get; init; }
    }

    private sealed record EbayPricingSummary
    {
        [JsonPropertyName("price")]
        public EbayAmount? Price { get; init; }
    }

    private sealed record EbayAmount
    {
        [JsonPropertyName("value")]
        public string Value { get; init; } = "0.00";

        [JsonPropertyName("currency")]
        public string Currency { get; init; } = "USD";
    }

    private sealed record EbayCreateOfferResponse
    {
        [JsonPropertyName("offerId")]
        public string? OfferId { get; init; }
    }

    private sealed record EbayPublishOfferResponse
    {
        [JsonPropertyName("listingId")]
        public string? ListingId { get; init; }
    }
}
