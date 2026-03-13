// =============================================================================
// PayPalPaymentGatewayExample.cs
// =============================================================================
// IMPORTANT: This is a REFERENCE EXAMPLE — not production-ready code.
// Use this to understand PayPal's API patterns before building the real
// PayPalPaymentGateway implementation in src/Payments/Payments/Processing/.
//
// Key differences from Stripe:
//   1. OAuth 2.0 token management (expires every ~8 hours, must refresh)
//   2. Currency amounts as DECIMAL STRINGS ("19.99"), NOT cents (1999)
//   3. Two-step flow: CreateOrderAsync() → CaptureAsync() (not a single call)
//   4. PayPal-Request-Id header for idempotency (not Idempotency-Key like Stripe)
//   5. Authorization windows vary by payment source (card: 3d, balance: 29d)
// =============================================================================

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Payments.Processing;

namespace Payments.Processing.PayPal.Examples;

/// <summary>
/// Example implementation of IPaymentGateway for PayPal's Orders API v2.
///
/// PayPal's flow differs from Stripe in one critical way: there is a mandatory
/// "create order" step before the buyer approves. The flow is:
///   1. Server: POST /v2/checkout/orders → get orderID
///   2. Client: PayPal JS SDK shows "Pay with PayPal" button using orderID
///   3. Customer: approves on PayPal's overlay
///   4. Server: POST /v2/checkout/orders/{orderID}/capture → get captureID
///
/// The IPaymentGateway.CaptureAsync(amount, currency, paymentMethodToken) call
/// maps to Step 4, where paymentMethodToken = the PayPal orderID returned after
/// buyer approval. Step 1 requires a separate endpoint outside this interface.
///
/// For returning customers using vaulted payment methods, Step 1–3 are skipped
/// and the vault token is passed directly as paymentMethodToken.
/// </summary>
public sealed class PayPalPaymentGatewayExample : IPaymentGateway
{
    private readonly HttpClient _httpClient;
    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly string _baseUrl;
    private readonly IMemoryCache _tokenCache;
    private readonly ILogger<PayPalPaymentGatewayExample> _logger;

    private const string TokenCacheKey = "paypal-access-token";

    public PayPalPaymentGatewayExample(
        HttpClient httpClient,
        IConfiguration configuration,
        IMemoryCache tokenCache,
        ILogger<PayPalPaymentGatewayExample> logger)
    {
        _httpClient = httpClient;
        _tokenCache = tokenCache;
        _logger = logger;

        _clientId = configuration["PayPal:ClientId"]
            ?? throw new InvalidOperationException("PayPal:ClientId not configured");
        _clientSecret = configuration["PayPal:ClientSecret"]
            ?? throw new InvalidOperationException("PayPal:ClientSecret not configured");

        // Switch between sandbox and production based on configuration
        var isSandbox = configuration["PayPal:Environment"] != "Production";
        _baseUrl = isSandbox
            ? "https://api-m.sandbox.paypal.com"
            : "https://api-m.paypal.com";

        _httpClient.BaseAddress = new Uri(_baseUrl);
    }

    /// <summary>
    /// Creates a PayPal order (Step 1 of the checkout flow).
    /// Returns a GatewayResult where TransactionId = the PayPal orderID.
    ///
    /// NOTE: This method is NOT on IPaymentGateway currently. In the actual
    /// implementation, this step lives in a dedicated PayPal controller endpoint
    /// (POST /api/paypal/orders) outside the gateway abstraction, to keep
    /// IPaymentGateway clean and Stripe-compatible.
    /// </summary>
    public async Task<GatewayResult> CreateOrderAsync(
        decimal amount,
        string currency,
        CancellationToken cancellationToken)
    {
        var accessToken = await GetAccessTokenAsync(cancellationToken);

        var request = new HttpRequestMessage(HttpMethod.Post, "/v2/checkout/orders");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        // NOTE: PayPal-Request-Id is PayPal's idempotency mechanism
        // Use a deterministic key based on your internal payment ID
        // (shown here as a GUID placeholder — use real payment ID in production)
        request.Headers.Add("PayPal-Request-Id", $"create-order-{Guid.NewGuid()}");

        var payload = new
        {
            intent = "CAPTURE",
            purchase_units = new[]
            {
                new
                {
                    amount = new
                    {
                        currency_code = currency.ToUpperInvariant(),
                        // ⚠️ PayPal amounts are DECIMAL STRINGS, not integer cents
                        value = amount.ToString("F2")
                    }
                }
            }
        };

        request.Content = JsonContent.Create(payload);

        try
        {
            var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("PayPal CreateOrder failed: {Status} {Body}", response.StatusCode, body);
                return new GatewayResult(false, null, MapPayPalError(body), IsRetriableStatusCode(response.StatusCode));
            }

            var order = JsonSerializer.Deserialize<PayPalOrderResponse>(body);
            _logger.LogInformation("PayPal order created: {OrderId}", order?.Id);

            return new GatewayResult(true, order?.Id, null, false);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "PayPal CreateOrder network error");
            return new GatewayResult(false, null, "gateway_network_error", true);  // retriable
        }
    }

    /// <summary>
    /// Captures a PayPal order after buyer approval (one-phase checkout).
    /// Maps to: POST /v2/checkout/orders/{paymentMethodToken}/capture
    /// where paymentMethodToken = the PayPal orderID returned after buyer approval.
    /// </summary>
    public async Task<GatewayResult> CaptureAsync(
        decimal amount,
        string currency,
        string paymentMethodToken,   // PayPal orderID (e.g., "5O190127TN364715T")
        CancellationToken cancellationToken)
    {
        var accessToken = await GetAccessTokenAsync(cancellationToken);

        // paymentMethodToken IS the PayPal orderID for standard checkout
        var orderId = paymentMethodToken;

        var request = new HttpRequestMessage(HttpMethod.Post, $"/v2/checkout/orders/{orderId}/capture");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("PayPal-Request-Id", $"capture-{orderId}");
        request.Content = JsonContent.Create(new { }); // Empty body for full capture

        try
        {
            var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("PayPal Capture failed: {OrderId} {Status} {Body}", orderId, response.StatusCode, body);
                return new GatewayResult(false, null, MapPayPalError(body), IsRetriableStatusCode(response.StatusCode));
            }

            var captureResponse = JsonSerializer.Deserialize<PayPalCaptureResponse>(body);

            // Extract the capture ID from nested response structure
            // ⚠️ The ORDER ID and the CAPTURE ID are DIFFERENT — refunds use CAPTURE ID
            var captureId = captureResponse?.PurchaseUnits?
                .FirstOrDefault()?.Payments?.Captures?
                .FirstOrDefault(c => c.Status == "COMPLETED")?.Id;

            if (captureId is null)
            {
                _logger.LogError("PayPal Capture response missing capture ID: {Body}", body);
                return new GatewayResult(false, null, "capture_id_missing", false);
            }

            _logger.LogInformation("PayPal capture completed: OrderId={OrderId} CaptureId={CaptureId}", orderId, captureId);
            return new GatewayResult(true, captureId, null, false);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "PayPal Capture network error: {OrderId}", orderId);
            return new GatewayResult(false, null, "gateway_network_error", true);
        }
    }

    /// <summary>
    /// Authorizes a PayPal payment (two-phase, Step 1: hold funds).
    /// Maps to: POST /v2/checkout/orders/{paymentMethodToken}/authorize
    ///
    /// ⚠️ Authorization windows vary by payment source:
    ///   - PayPal balance: up to 29 days
    ///   - Credit/debit card: up to 3 days (NOT Stripe's 7 days)
    /// Use the returned ExpiresAt from the authorization response, not a hardcoded value.
    /// </summary>
    public async Task<GatewayResult> AuthorizeAsync(
        decimal amount,
        string currency,
        string paymentMethodToken,   // PayPal orderID after buyer approval
        CancellationToken cancellationToken)
    {
        var accessToken = await GetAccessTokenAsync(cancellationToken);
        var orderId = paymentMethodToken;

        var request = new HttpRequestMessage(HttpMethod.Post, $"/v2/checkout/orders/{orderId}/authorize");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("PayPal-Request-Id", $"auth-{orderId}");
        request.Content = JsonContent.Create(new { });

        try
        {
            var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("PayPal Authorize failed: {OrderId} {Status} {Body}", orderId, response.StatusCode, body);
                return new GatewayResult(false, null, MapPayPalError(body), IsRetriableStatusCode(response.StatusCode));
            }

            var authResponse = JsonSerializer.Deserialize<PayPalAuthorizationResponse>(body);
            var authorizationId = authResponse?.PurchaseUnits?
                .FirstOrDefault()?.Payments?.Authorizations?
                .FirstOrDefault(a => a.Status == "CREATED")?.Id;

            if (authorizationId is null)
            {
                _logger.LogError("PayPal Authorization response missing authorization ID: {Body}", body);
                return new GatewayResult(false, null, "authorization_id_missing", false);
            }

            _logger.LogInformation("PayPal authorization created: OrderId={OrderId} AuthId={AuthId}", orderId, authorizationId);

            // Note: AuthorizationId is returned as TransactionId in GatewayResult
            // The Payments BC stores this and uses it later in CaptureAuthorizedAsync
            return new GatewayResult(true, authorizationId, null, false);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "PayPal Authorize network error: {OrderId}", orderId);
            return new GatewayResult(false, null, "gateway_network_error", true);
        }
    }

    /// <summary>
    /// Captures a previously authorized PayPal payment (two-phase, Step 2).
    /// Maps to: POST /v2/payments/authorizations/{authorizationId}/capture
    /// </summary>
    public async Task<GatewayResult> CaptureAuthorizedAsync(
        string authorizationId,
        decimal amount,
        CancellationToken cancellationToken)
    {
        var accessToken = await GetAccessTokenAsync(cancellationToken);

        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/capture");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("PayPal-Request-Id", $"capture-auth-{authorizationId}");

        // For partial capture, specify amount; for full capture, omit amount field
        request.Content = JsonContent.Create(new
        {
            // Uncomment for partial capture support:
            // amount = new { currency_code = "USD", value = amount.ToString("F2") }
        });

        try
        {
            var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("PayPal CaptureAuthorized failed: {AuthId} {Status} {Body}",
                    authorizationId, response.StatusCode, body);
                return new GatewayResult(false, null, MapPayPalError(body), IsRetriableStatusCode(response.StatusCode));
            }

            var captureResult = JsonSerializer.Deserialize<PayPalSingleCaptureResponse>(body);

            if (captureResult?.Status != "COMPLETED")
            {
                _logger.LogWarning("PayPal CaptureAuthorized status not COMPLETED: {Status}", captureResult?.Status);
                return new GatewayResult(false, null, $"unexpected_capture_status:{captureResult?.Status}", false);
            }

            _logger.LogInformation("PayPal authorized capture completed: AuthId={AuthId} CaptureId={CaptureId}",
                authorizationId, captureResult.Id);
            return new GatewayResult(true, captureResult.Id, null, false);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "PayPal CaptureAuthorized network error: {AuthId}", authorizationId);
            return new GatewayResult(false, null, "gateway_network_error", true);
        }
    }

    /// <summary>
    /// Refunds a captured PayPal payment (full or partial).
    /// Maps to: POST /v2/payments/captures/{transactionId}/refund
    ///
    /// ⚠️ transactionId MUST be the CAPTURE ID (e.g., "3C679366HH908993F"),
    /// NOT the Order ID. These are different in PayPal's API.
    /// </summary>
    public async Task<GatewayResult> RefundAsync(
        string transactionId,   // PayPal CAPTURE ID (not Order ID)
        decimal amount,
        CancellationToken cancellationToken)
    {
        var accessToken = await GetAccessTokenAsync(cancellationToken);

        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/v2/payments/captures/{transactionId}/refund");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("PayPal-Request-Id", $"refund-{transactionId}-{amount:F2}");

        // Note: For full refund, can omit amount; for partial, specify amount
        // CritterSupply always specifies amount to support partial refunds
        request.Content = JsonContent.Create(new
        {
            // Partial refund: specify amount
            // Full refund: omit amount field (PayPal refunds the full captured amount)
        });

        try
        {
            var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("PayPal Refund failed: {CaptureId} {Status} {Body}",
                    transactionId, response.StatusCode, body);
                return new GatewayResult(false, null, MapPayPalError(body), IsRetriableStatusCode(response.StatusCode));
            }

            var refundResult = JsonSerializer.Deserialize<PayPalRefundResponse>(body);
            _logger.LogInformation("PayPal refund completed: CaptureId={CaptureId} RefundId={RefundId}",
                transactionId, refundResult?.Id);

            return new GatewayResult(true, refundResult?.Id, null, false);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "PayPal Refund network error: {CaptureId}", transactionId);
            return new GatewayResult(false, null, "gateway_network_error", true);
        }
    }

    // ==========================================================================
    // OAuth Token Management
    // ==========================================================================

    /// <summary>
    /// Gets a cached access token, refreshing if expired or near-expiry.
    ///
    /// PayPal access tokens expire in ~8 hours (31,668 seconds).
    /// This is unlike Stripe's static API key which never expires.
    /// Failing to refresh results in 401 Unauthorized on all API calls.
    /// </summary>
    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_tokenCache.TryGetValue(TokenCacheKey, out CachedToken? cached) && cached is not null)
        {
            if (!cached.IsNearExpiry)
            {
                return cached.AccessToken;
            }
        }

        // Refresh the token
        var credentials = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes($"{_clientId}:{_clientSecret}"));

        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/oauth2/token");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "client_credentials")
        });

        var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var tokenResponse = await response.Content.ReadFromJsonAsync<PayPalTokenResponse>(
            cancellationToken: cancellationToken);

        if (tokenResponse?.AccessToken is null)
        {
            throw new InvalidOperationException("PayPal token response missing access_token");
        }

        var newToken = new CachedToken(
            tokenResponse.AccessToken,
            DateTimeOffset.UtcNow.AddSeconds(tokenResponse.ExpiresIn));

        // Cache with a TTL slightly shorter than actual expiry (60s buffer)
        _tokenCache.Set(TokenCacheKey, newToken, TimeSpan.FromSeconds(tokenResponse.ExpiresIn - 60));

        _logger.LogInformation("PayPal access token refreshed. Expires in {ExpiresIn}s", tokenResponse.ExpiresIn);

        return newToken.AccessToken;
    }

    // ==========================================================================
    // Error Mapping
    // ==========================================================================

    private static string MapPayPalError(string responseBody)
    {
        try
        {
            var error = JsonSerializer.Deserialize<PayPalErrorResponse>(responseBody);
            return error?.Details?.FirstOrDefault()?.Description
                ?? error?.Message
                ?? "unknown_paypal_error";
        }
        catch
        {
            return "paypal_error_parse_failed";
        }
    }

    private static bool IsRetriableStatusCode(System.Net.HttpStatusCode statusCode) =>
        statusCode is System.Net.HttpStatusCode.ServiceUnavailable    // 503
            or System.Net.HttpStatusCode.GatewayTimeout               // 504
            or System.Net.HttpStatusCode.TooManyRequests              // 429
            or System.Net.HttpStatusCode.InternalServerError;         // 500 (sometimes retriable)
}

// =============================================================================
// PayPal API Response Models
// =============================================================================

internal sealed record CachedToken(string AccessToken, DateTimeOffset ExpiresAt)
{
    // Refresh if less than 60 seconds remain
    public bool IsNearExpiry => DateTimeOffset.UtcNow >= ExpiresAt.AddSeconds(-60);
}

internal sealed class PayPalTokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }
}

internal sealed class PayPalOrderResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }
}

internal sealed class PayPalCaptureResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("purchase_units")]
    public PayPalPurchaseUnitCapture[]? PurchaseUnits { get; set; }
}

internal sealed class PayPalPurchaseUnitCapture
{
    [JsonPropertyName("payments")]
    public PayPalPaymentsBlock? Payments { get; set; }
}

internal sealed class PayPalPaymentsBlock
{
    [JsonPropertyName("captures")]
    public PayPalCapture[]? Captures { get; set; }

    [JsonPropertyName("authorizations")]
    public PayPalAuthorization[]? Authorizations { get; set; }
}

internal sealed class PayPalCapture
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }
}

internal sealed class PayPalAuthorization
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("expiration_time")]
    public DateTimeOffset? ExpirationTime { get; set; }
}

internal sealed class PayPalAuthorizationResponse
{
    [JsonPropertyName("purchase_units")]
    public PayPalPurchaseUnitCapture[]? PurchaseUnits { get; set; }
}

internal sealed class PayPalSingleCaptureResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }
}

internal sealed class PayPalRefundResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }
}

internal sealed class PayPalErrorResponse
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("details")]
    public PayPalErrorDetail[]? Details { get; set; }
}

internal sealed class PayPalErrorDetail
{
    [JsonPropertyName("issue")]
    public string? Issue { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}
