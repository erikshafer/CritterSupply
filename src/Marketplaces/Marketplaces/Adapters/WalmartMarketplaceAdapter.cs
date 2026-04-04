using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Marketplaces.Credentials;
using Microsoft.Extensions.Logging;

namespace Marketplaces.Adapters;

/// <summary>
/// Production Walmart Marketplace API adapter that submits listings via the Item feed endpoint.
/// <para>
/// Authentication uses OAuth 2.0 client credentials grant:
/// <list type="number">
/// <item>Exchange client_id + client_secret (HTTP Basic) for an access token</item>
/// <item>Use the access token as <c>WM_SEC.ACCESS_TOKEN</c> header on API calls</item>
/// </list>
/// See ADR 0053 for the complete authentication flow documentation.
/// </para>
/// <para>
/// Credentials are retrieved from <see cref="IVaultClient"/> using vault path conventions
/// defined in ADR 0051 (e.g., <c>walmart/client-id</c>, <c>walmart/client-secret</c>).
/// </para>
/// </summary>
public sealed class WalmartMarketplaceAdapter : IMarketplaceAdapter
{
    private readonly IVaultClient _vault;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WalmartMarketplaceAdapter> _logger;

    // OAuth token cache — avoids repeated vault reads and token exchanges per request
    private string? _cachedAccessToken;
    private DateTimeOffset _tokenExpiry = DateTimeOffset.MinValue;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    // Walmart Marketplace API endpoints
    private const string TokenUrl = "https://marketplace.walmartapis.com/v3/token";
    private const string FeedsBaseUrl = "https://marketplace.walmartapis.com/v3/feeds";

    public string ChannelCode => "WALMART_US";

    public WalmartMarketplaceAdapter(
        IVaultClient vault,
        IHttpClientFactory httpClientFactory,
        ILogger<WalmartMarketplaceAdapter> logger)
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
            var sellerId = await _vault.GetSecretAsync("walmart/seller-id", ct);

            var client = _httpClientFactory.CreateClient("WalmartApi");

            // Walmart Item feed submission — POST /v3/feeds?feedType=MP_ITEM
            var requestUrl = $"{FeedsBaseUrl}?feedType=MP_ITEM";

            var feedBody = BuildItemFeedBody(submission);

            using var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);
            request.Headers.Add("WM_SEC.ACCESS_TOKEN", accessToken);
            request.Headers.Add("WM_CONSUMER.ID", sellerId);
            request.Headers.Add("WM_SVC.NAME", "Walmart Marketplace");
            request.Headers.Add("WM_QOS.CORRELATION_ID", Guid.NewGuid().ToString());
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Content = JsonContent.Create(feedBody, options: JsonOptions);

            _logger.LogInformation(
                "Submitting listing to Walmart Marketplace API: SKU={Sku}, SellerId={SellerId}",
                submission.Sku, sellerId);

            using var response = await client.SendAsync(request, ct);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<WalmartFeedResponse>(JsonOptions, ct);
                var feedId = result?.FeedId ?? Guid.NewGuid().ToString();

                _logger.LogInformation(
                    "Walmart feed submitted successfully: SKU={Sku}, FeedId={FeedId}",
                    submission.Sku, feedId);

                return new SubmissionResult(
                    IsSuccess: true,
                    ExternalSubmissionId: $"wmrt-{feedId}");
            }

            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning(
                "Walmart feed submission failed: SKU={Sku}, StatusCode={StatusCode}, Body={Body}",
                submission.Sku, (int)response.StatusCode, errorBody);

            return new SubmissionResult(
                IsSuccess: false,
                ExternalSubmissionId: null,
                ErrorMessage: $"Walmart API returned {(int)response.StatusCode}: {errorBody}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to submit listing to Walmart Marketplace API: SKU={Sku}", submission.Sku);
            return new SubmissionResult(
                IsSuccess: false,
                ExternalSubmissionId: null,
                ErrorMessage: $"Walmart adapter error: {ex.Message}");
        }
    }

    public async Task<SubmissionStatus> CheckSubmissionStatusAsync(
        string externalSubmissionId,
        CancellationToken ct = default)
    {
        // Strip the wmrt- prefix to get the raw Walmart feed ID
        var feedId = externalSubmissionId.StartsWith("wmrt-", StringComparison.OrdinalIgnoreCase)
            ? externalSubmissionId["wmrt-".Length..]
            : externalSubmissionId;

        try
        {
            var accessToken = await GetAccessTokenAsync(ct);
            var sellerId = await _vault.GetSecretAsync("walmart/seller-id", ct);

            var client = _httpClientFactory.CreateClient("WalmartApi");

            using var request = new HttpRequestMessage(HttpMethod.Get, $"{FeedsBaseUrl}/{feedId}");
            request.Headers.Add("WM_SEC.ACCESS_TOKEN", accessToken);
            request.Headers.Add("WM_CONSUMER.ID", sellerId);
            request.Headers.Add("WM_SVC.NAME", "Walmart Marketplace");
            request.Headers.Add("WM_QOS.CORRELATION_ID", Guid.NewGuid().ToString());
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            _logger.LogInformation(
                "Polling Walmart feed status: FeedId={FeedId}, SubmissionId={SubmissionId}",
                feedId, externalSubmissionId);

            using var response = await client.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning(
                    "Walmart feed status poll failed: FeedId={FeedId}, StatusCode={StatusCode}, Body={Body}",
                    feedId, (int)response.StatusCode, errorBody);

                return new SubmissionStatus(
                    ExternalSubmissionId: externalSubmissionId,
                    IsLive: false,
                    IsFailed: true,
                    FailureReason: $"Walmart feed status API returned {(int)response.StatusCode}: {errorBody}");
            }

            var feedResponse = await response.Content.ReadFromJsonAsync<WalmartFeedStatusResponse>(JsonOptions, ct);
            var feedStatus = feedResponse?.FeedStatus ?? string.Empty;

            _logger.LogInformation(
                "Walmart feed status: FeedId={FeedId}, FeedStatus={FeedStatus}",
                feedId, feedStatus);

            return feedStatus.ToUpperInvariant() switch
            {
                "PROCESSED" => new SubmissionStatus(
                    ExternalSubmissionId: externalSubmissionId,
                    IsLive: true,
                    IsFailed: false),
                "ERROR" => new SubmissionStatus(
                    ExternalSubmissionId: externalSubmissionId,
                    IsLive: false,
                    IsFailed: true,
                    FailureReason: "Feed processing error"),
                // RECEIVED or INPROGRESS — still pending, reschedule
                _ => new SubmissionStatus(
                    ExternalSubmissionId: externalSubmissionId,
                    IsLive: false,
                    IsFailed: false)
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "Failed to check Walmart feed status: SubmissionId={SubmissionId}", externalSubmissionId);

            return new SubmissionStatus(
                ExternalSubmissionId: externalSubmissionId,
                IsLive: false,
                IsFailed: true,
                FailureReason: $"Walmart adapter error checking feed status: {ex.Message}");
        }
    }

    public async Task<bool> DeactivateListingAsync(
        string externalListingId,
        CancellationToken ct = default)
    {
        // externalListingId format: wmrt-{sku} (see ADR 0057 for the two-identifier design)
        var sku = externalListingId.StartsWith("wmrt-", StringComparison.OrdinalIgnoreCase)
            ? externalListingId["wmrt-".Length..]
            : externalListingId;

        try
        {
            var accessToken = await GetAccessTokenAsync(ct);
            var sellerId = await _vault.GetSecretAsync("walmart/seller-id", ct);

            var client = _httpClientFactory.CreateClient("WalmartApi");

            var requestUrl = $"{FeedsBaseUrl}?feedType=RETIRE_ITEM";

            var feedBody = BuildRetireFeedBody(sku);

            using var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);
            request.Headers.Add("WM_SEC.ACCESS_TOKEN", accessToken);
            request.Headers.Add("WM_CONSUMER.ID", sellerId);
            request.Headers.Add("WM_SVC.NAME", "Walmart Marketplace");
            request.Headers.Add("WM_QOS.CORRELATION_ID", Guid.NewGuid().ToString());
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Content = JsonContent.Create(feedBody, options: JsonOptions);

            _logger.LogInformation(
                "Submitting RETIRE_ITEM feed to Walmart Marketplace API: SKU={Sku}, ExternalListingId={ExternalListingId}",
                sku, externalListingId);

            using var response = await client.SendAsync(request, ct);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation(
                    "Walmart RETIRE_ITEM feed accepted: SKU={Sku}",
                    sku);
                return true;
            }

            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning(
                "Walmart RETIRE_ITEM feed failed: SKU={Sku}, StatusCode={StatusCode}, Body={Body}",
                sku, (int)response.StatusCode, errorBody);
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to submit RETIRE_ITEM feed to Walmart Marketplace API: SKU={Sku}", sku);
            return false;
        }
    }

    /// <summary>
    /// Obtains an OAuth access token via client credentials grant, using cached token if still valid.
    /// Walmart tokens typically have a 15-minute TTL; we refresh 5 minutes before expiry.
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

            var clientId = await _vault.GetSecretAsync("walmart/client-id", ct);
            var clientSecret = await _vault.GetSecretAsync("walmart/client-secret", ct);

            var client = _httpClientFactory.CreateClient("WalmartApi");

            // Walmart OAuth uses HTTP Basic auth: Base64(client_id:client_secret)
            var credentials = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));

            var tokenRequest = new HttpRequestMessage(HttpMethod.Post, TokenUrl);
            tokenRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            tokenRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            tokenRequest.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials"
            });

            using var response = await client.SendAsync(tokenRequest, ct);
            response.EnsureSuccessStatusCode();

            var tokenResponse = await response.Content.ReadFromJsonAsync<WalmartTokenResponse>(JsonOptions, ct);

            _cachedAccessToken = tokenResponse?.AccessToken
                ?? throw new InvalidOperationException("Walmart token response missing access_token");

            // Cache with 5-minute safety margin before actual expiry
            var expiresInSeconds = tokenResponse.ExpiresIn > 0 ? tokenResponse.ExpiresIn : 900;
            _tokenExpiry = DateTimeOffset.UtcNow.AddSeconds(expiresInSeconds - 300);

            _logger.LogInformation(
                "Walmart OAuth access token refreshed successfully. Expires at {Expiry}.",
                _tokenExpiry);

            return _cachedAccessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    /// <summary>
    /// Builds the Walmart MP_ITEM feed body for a single item.
    /// </summary>
    private static WalmartItemFeed BuildItemFeedBody(ListingSubmission submission)
    {
        var item = new WalmartFeedItem
        {
            Sku = submission.Sku,
            ProductName = submission.ProductName,
            Price = submission.Price,
            Description = submission.Description,
            Category = submission.Category
        };

        return new WalmartItemFeed
        {
            Items = [item]
        };
    }

    /// <summary>
    /// Builds the Walmart RETIRE_ITEM feed body for a single item.
    /// The RETIRE_ITEM feed requires only the SKU to identify the item to retire.
    /// </summary>
    private static WalmartRetireFeed BuildRetireFeedBody(string sku)
    {
        return new WalmartRetireFeed
        {
            Items = [new WalmartRetireItem { Sku = sku }]
        };
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    // --- Walmart API DTOs (internal to adapter) ---

    private sealed record WalmartFeedStatusResponse
    {
        [JsonPropertyName("feedId")]
        public string? FeedId { get; init; }

        [JsonPropertyName("feedStatus")]
        public string? FeedStatus { get; init; }
    }

    private sealed record WalmartTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; init; }

        [JsonPropertyName("token_type")]
        public string? TokenType { get; init; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; init; }
    }

    private sealed record WalmartFeedResponse
    {
        [JsonPropertyName("feedId")]
        public string? FeedId { get; init; }
    }

    private sealed record WalmartItemFeed
    {
        [JsonPropertyName("items")]
        public WalmartFeedItem[] Items { get; init; } = [];
    }

    private sealed record WalmartRetireFeed
    {
        [JsonPropertyName("items")]
        public WalmartRetireItem[] Items { get; init; } = [];
    }

    private sealed record WalmartRetireItem
    {
        [JsonPropertyName("sku")]
        public string Sku { get; init; } = string.Empty;
    }

    private sealed record WalmartFeedItem
    {
        [JsonPropertyName("sku")]
        public string Sku { get; init; } = string.Empty;

        [JsonPropertyName("productName")]
        public string ProductName { get; init; } = string.Empty;

        [JsonPropertyName("price")]
        public decimal Price { get; init; }

        [JsonPropertyName("description")]
        public string? Description { get; init; }

        [JsonPropertyName("category")]
        public string? Category { get; init; }
    }
}
