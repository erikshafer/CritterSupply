using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Marketplaces.Credentials;
using Microsoft.Extensions.Logging;

namespace Marketplaces.Adapters;

/// <summary>
/// Production Amazon SP-API adapter that submits listings via the Selling Partner API.
/// <para>
/// Authentication uses Login with Amazon (LWA) OAuth 2.0:
/// <list type="number">
/// <item>Exchange client credentials + refresh token for an LWA access token</item>
/// <item>Use the access token as Bearer token on SP-API calls</item>
/// </list>
/// See ADR 0052 for the complete authentication flow documentation.
/// </para>
/// <para>
/// Credentials are retrieved from <see cref="IVaultClient"/> using vault path conventions
/// defined in ADR 0051 (e.g., <c>amazon/client-id</c>, <c>amazon/client-secret</c>).
/// </para>
/// </summary>
public sealed class AmazonMarketplaceAdapter : IMarketplaceAdapter
{
    private readonly IVaultClient _vault;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AmazonMarketplaceAdapter> _logger;

    // LWA token cache — avoids repeated vault reads and token exchanges per request
    private string? _cachedAccessToken;
    private DateTimeOffset _tokenExpiry = DateTimeOffset.MinValue;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    // SP-API base URL for North America region
    private const string SpApiBaseUrl = "https://sellingpartnerapi-na.amazon.com";
    private const string LwaTokenUrl = "https://api.amazon.com/auth/o2/token";

    public string ChannelCode => "AMAZON_US";

    public AmazonMarketplaceAdapter(
        IVaultClient vault,
        IHttpClientFactory httpClientFactory,
        ILogger<AmazonMarketplaceAdapter> logger)
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
            var sellerId = await _vault.GetSecretAsync("amazon/seller-id", ct);
            var marketplaceId = await _vault.GetSecretAsync("amazon/marketplace-id", ct);

            var client = _httpClientFactory.CreateClient("AmazonSpApi");

            // SP-API Listings Items API — PUT /listings/2021-08-01/items/{sellerId}/{sku}
            // Reference: https://developer-docs.amazon.com/sp-api/docs/listings-items-api-v2021-08-01-reference
            var requestUrl = $"{SpApiBaseUrl}/listings/2021-08-01/items/{sellerId}/{Uri.EscapeDataString(submission.Sku)}";

            var requestBody = BuildListingsItemPutBody(submission, marketplaceId);

            using var request = new HttpRequestMessage(HttpMethod.Put, requestUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Add("x-amz-access-token", accessToken);
            request.Content = JsonContent.Create(requestBody, options: JsonOptions);

            _logger.LogInformation(
                "Submitting listing to Amazon SP-API: SKU={Sku}, Seller={SellerId}, Marketplace={MarketplaceId}",
                submission.Sku, sellerId, marketplaceId);

            using var response = await client.SendAsync(request, ct);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<SpApiListingsResponse>(JsonOptions, ct);
                var submissionId = result?.Sku ?? submission.Sku;

                _logger.LogInformation(
                    "Amazon SP-API listing submitted successfully: SKU={Sku}, Status={Status}",
                    submission.Sku, result?.Status);

                return new SubmissionResult(
                    IsSuccess: true,
                    ExternalSubmissionId: $"amzn-{submissionId}");
            }

            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning(
                "Amazon SP-API listing submission failed: SKU={Sku}, StatusCode={StatusCode}, Body={Body}",
                submission.Sku, (int)response.StatusCode, errorBody);

            return new SubmissionResult(
                IsSuccess: false,
                ExternalSubmissionId: null,
                ErrorMessage: $"SP-API returned {(int)response.StatusCode}: {errorBody}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to submit listing to Amazon SP-API: SKU={Sku}", submission.Sku);
            return new SubmissionResult(
                IsSuccess: false,
                ExternalSubmissionId: null,
                ErrorMessage: $"Amazon adapter error: {ex.Message}");
        }
    }

    public Task<SubmissionStatus> CheckSubmissionStatusAsync(
        string externalSubmissionId,
        CancellationToken ct = default)
    {
        // Skeleton implementation — real status polling deferred to M38.x (D-3).
        // SP-API uses the Feeds API for async submission; the Listings Items API PUT
        // returns immediate validation results. A full implementation would call
        // GET /listings/2021-08-01/items/{sellerId}/{sku} to verify the listing
        // is active on the marketplace.
        _logger.LogInformation(
            "CheckSubmissionStatusAsync not yet implemented for Amazon SP-API. " +
            "Returning pending status for submission {SubmissionId}.",
            externalSubmissionId);

        return Task.FromResult(new SubmissionStatus(
            ExternalSubmissionId: externalSubmissionId,
            IsLive: false,
            IsFailed: false,
            FailureReason: "Status polling not yet implemented — deferred to M38.x"));
    }

    public async Task<bool> DeactivateListingAsync(
        string externalListingId,
        CancellationToken ct = default)
    {
        // Strip the "amzn-" prefix to get the raw SKU.
        var sku = externalListingId.StartsWith("amzn-", StringComparison.OrdinalIgnoreCase)
            ? externalListingId["amzn-".Length..]
            : externalListingId;

        try
        {
            var accessToken = await GetAccessTokenAsync(ct);
            var sellerId = await _vault.GetSecretAsync("amazon/seller-id", ct);
            var marketplaceId = await _vault.GetSecretAsync("amazon/marketplace-id", ct);

            var client = _httpClientFactory.CreateClient("AmazonSpApi");

            // SP-API PATCH — delete purchasable_offer to deactivate the listing.
            // This removes the listing from the marketplace without deleting the ASIN.
            // Reference: https://developer-docs.amazon.com/sp-api/docs/listings-items-api-v2021-08-01-reference
            var requestUrl = $"{SpApiBaseUrl}/listings/2021-08-01/items/{sellerId}/{Uri.EscapeDataString(sku)}" +
                             $"?marketplaceIds={Uri.EscapeDataString(marketplaceId)}";

            var patchBody = new
            {
                productType = "PRODUCT",
                patches = new[]
                {
                    new
                    {
                        op = "delete",
                        path = "/attributes/purchasable_offer",
                        value = Array.Empty<object>()
                    }
                }
            };

            using var request = new HttpRequestMessage(new HttpMethod("PATCH"), requestUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Add("x-amz-access-token", accessToken);
            request.Content = JsonContent.Create(patchBody, options: JsonOptions);

            _logger.LogInformation(
                "Deactivating Amazon SP-API listing: SKU={Sku}, Seller={SellerId}, Marketplace={MarketplaceId}",
                sku, sellerId, marketplaceId);

            using var response = await client.SendAsync(request, ct);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation(
                    "Amazon SP-API listing deactivated successfully: SKU={Sku}",
                    sku);
                return true;
            }

            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning(
                "Amazon SP-API listing deactivation failed: SKU={Sku}, StatusCode={StatusCode}, Body={Body}",
                sku, (int)response.StatusCode, errorBody);
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to deactivate Amazon SP-API listing: SKU={Sku}", sku);
            return false;
        }
    }

    /// <summary>
    /// Obtains an LWA access token, using cached token if still valid.
    /// LWA tokens typically have a 1-hour TTL; we refresh 5 minutes before expiry.
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

            var clientId = await _vault.GetSecretAsync("amazon/client-id", ct);
            var clientSecret = await _vault.GetSecretAsync("amazon/client-secret", ct);
            var refreshToken = await _vault.GetSecretAsync("amazon/refresh-token", ct);

            var client = _httpClientFactory.CreateClient("AmazonSpApi");

            var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["refresh_token"] = refreshToken
            });

            using var response = await client.PostAsync(LwaTokenUrl, tokenRequest, ct);
            response.EnsureSuccessStatusCode();

            var tokenResponse = await response.Content.ReadFromJsonAsync<LwaTokenResponse>(JsonOptions, ct);

            _cachedAccessToken = tokenResponse?.AccessToken
                ?? throw new InvalidOperationException("LWA token response missing access_token");

            // Cache with 5-minute safety margin before actual expiry
            var expiresInSeconds = tokenResponse.ExpiresIn > 0 ? tokenResponse.ExpiresIn : 3600;
            _tokenExpiry = DateTimeOffset.UtcNow.AddSeconds(expiresInSeconds - 300);

            _logger.LogInformation(
                "LWA access token refreshed successfully. Expires at {Expiry}.",
                _tokenExpiry);

            return _cachedAccessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    /// <summary>
    /// Builds the SP-API Listings Items API PUT request body.
    /// Reference: https://developer-docs.amazon.com/sp-api/docs/listings-items-api-v2021-08-01-reference
    /// </summary>
    private static SpApiListingsItemPutRequest BuildListingsItemPutBody(
        ListingSubmission submission,
        string marketplaceId)
    {
        var attributes = new Dictionary<string, object>
        {
            ["item_name"] = new[] { new { value = submission.ProductName, marketplace_id = marketplaceId } },
            ["purchasable_offer"] = new[]
            {
                new
                {
                    marketplace_id = marketplaceId,
                    currency = "USD",
                    our_price = new[] { new { schedule = new[] { new { value_with_tax = submission.Price } } } }
                }
            }
        };

        if (!string.IsNullOrEmpty(submission.Description))
        {
            attributes["product_description"] = new[]
            {
                new { value = submission.Description, marketplace_id = marketplaceId }
            };
        }

        if (!string.IsNullOrEmpty(submission.Category))
        {
            attributes["recommended_browse_nodes"] = new[]
            {
                new { value = submission.Category, marketplace_id = marketplaceId }
            };
        }

        // Merge any channel-specific extensions into attributes
        if (submission.ChannelExtensions is not null)
        {
            foreach (var (key, value) in submission.ChannelExtensions)
            {
                attributes[key] = new[] { new { value, marketplace_id = marketplaceId } };
            }
        }

        return new SpApiListingsItemPutRequest
        {
            ProductType = "PRODUCT",
            Requirements = "LISTING",
            Attributes = attributes,
            MarketplaceIds = [marketplaceId]
        };
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    // --- SP-API DTOs (internal to adapter) ---

    private sealed record LwaTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; init; }

        [JsonPropertyName("token_type")]
        public string? TokenType { get; init; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; init; }
    }

    private sealed record SpApiListingsItemPutRequest
    {
        [JsonPropertyName("productType")]
        public string ProductType { get; init; } = "PRODUCT";

        [JsonPropertyName("requirements")]
        public string Requirements { get; init; } = "LISTING";

        [JsonPropertyName("attributes")]
        public Dictionary<string, object> Attributes { get; init; } = new();

        [JsonPropertyName("marketplaceIds")]
        public string[] MarketplaceIds { get; init; } = [];
    }

    private sealed record SpApiListingsResponse
    {
        [JsonPropertyName("sku")]
        public string? Sku { get; init; }

        [JsonPropertyName("status")]
        public string? Status { get; init; }

        [JsonPropertyName("submissionId")]
        public string? SubmissionId { get; init; }
    }
}
