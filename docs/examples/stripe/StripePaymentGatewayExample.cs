// Example Stripe Gateway Implementation
// Location: docs/examples/stripe/StripePaymentGatewayExample.cs
// Purpose: Reference implementation for Stripe API integration in CritterSupply

using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Payments.Processing.Examples;

/// <summary>
/// Example implementation of Stripe payment gateway.
/// This demonstrates the key patterns for integrating with Stripe's API:
/// - Authorization header with Bearer token
/// - Idempotency-Key header for safe retries
/// - FormUrlEncodedContent for API requests (Stripe expects form data, not JSON)
/// - Converting decimal amounts to cents (smallest currency unit)
/// - Mapping Stripe error responses to GatewayResult
/// </summary>
public sealed class StripePaymentGatewayExample : IPaymentGateway
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly ILogger<StripePaymentGatewayExample> _logger;

    public StripePaymentGatewayExample(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<StripePaymentGatewayExample> logger)
    {
        _httpClient = httpClient;
        _apiKey = configuration["Stripe:SecretKey"]
            ?? throw new InvalidOperationException("Stripe:SecretKey not configured");
        _logger = logger;

        // Configure HttpClient for Stripe API
        _httpClient.BaseAddress = new Uri("https://api.stripe.com");
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _apiKey);
    }

    /// <summary>
    /// Authorizes a payment (two-phase commit: authorize, then capture later).
    /// Maps to: POST /v1/payment_intents with capture_method: manual
    /// </summary>
    public async Task<GatewayResult> AuthorizeAsync(
        decimal amount,
        string currency,
        string paymentMethodToken,
        CancellationToken ct)
    {
        // Generate idempotency key for safe retries
        var idempotencyKey = $"auth-{Guid.NewGuid()}";

        try
        {
            // Stripe expects form-encoded data, not JSON
            var payload = new Dictionary<string, string>
            {
                ["amount"] = ConvertToStripeAmount(amount, currency).ToString(),
                ["currency"] = currency.ToLowerInvariant(),
                ["payment_method"] = paymentMethodToken,
                ["confirmation_method"] = "automatic",
                ["capture_method"] = "manual",  // Two-phase commit
                ["confirm"] = "true"  // Auto-confirm after creation
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/payment_intents");
            request.Headers.Add("Idempotency-Key", idempotencyKey);
            request.Content = new FormUrlEncodedContent(payload);

            var response = await _httpClient.SendAsync(request, ct);
            var content = await response.Content.ReadAsStringAsync(ct);

            // Handle error responses
            if (!response.IsSuccessStatusCode)
            {
                var error = JsonSerializer.Deserialize<StripeErrorResponse>(content);
                return MapStripeError(error);
            }

            var intent = JsonSerializer.Deserialize<StripePaymentIntentResponse>(content);

            // Status "requires_capture" means authorization succeeded
            if (intent.Status == "requires_capture")
            {
                _logger.LogInformation(
                    "Payment authorized: {IntentId} for {Amount} {Currency}",
                    intent.Id, amount, currency);

                return new GatewayResult(
                    Success: true,
                    TransactionId: intent.Id,
                    FailureReason: null,
                    IsRetriable: false);
            }

            // Other statuses indicate async processing needed
            _logger.LogWarning(
                "PaymentIntent in unexpected status: {Status} for {IntentId}",
                intent.Status, intent.Id);

            return new GatewayResult(
                Success: false,
                TransactionId: intent.Id,
                FailureReason: $"Payment requires action: {intent.Status}",
                IsRetriable: true);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error during Stripe authorization");
            return new GatewayResult(false, null, "Network error", IsRetriable: true);
        }
    }

    /// <summary>
    /// Captures funds immediately (one-phase: authorize and capture together).
    /// Maps to: POST /v1/payment_intents with capture_method: automatic
    /// </summary>
    public async Task<GatewayResult> CaptureAsync(
        decimal amount,
        string currency,
        string paymentMethodToken,
        CancellationToken ct)
    {
        var idempotencyKey = $"capture-{Guid.NewGuid()}";

        try
        {
            var payload = new Dictionary<string, string>
            {
                ["amount"] = ConvertToStripeAmount(amount, currency).ToString(),
                ["currency"] = currency.ToLowerInvariant(),
                ["payment_method"] = paymentMethodToken,
                ["confirmation_method"] = "automatic",
                ["capture_method"] = "automatic",  // One-phase commit
                ["confirm"] = "true"
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/payment_intents");
            request.Headers.Add("Idempotency-Key", idempotencyKey);
            request.Content = new FormUrlEncodedContent(payload);

            var response = await _httpClient.SendAsync(request, ct);
            var content = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                var error = JsonSerializer.Deserialize<StripeErrorResponse>(content);
                return MapStripeError(error);
            }

            var intent = JsonSerializer.Deserialize<StripePaymentIntentResponse>(content);

            if (intent.Status == "succeeded")
            {
                // Extract charge ID from latest_charge
                var chargeId = intent.LatestCharge ?? intent.Id;

                _logger.LogInformation(
                    "Payment captured: {ChargeId} for {Amount} {Currency}",
                    chargeId, amount, currency);

                return new GatewayResult(
                    Success: true,
                    TransactionId: chargeId,
                    FailureReason: null,
                    IsRetriable: false);
            }

            return new GatewayResult(
                Success: false,
                TransactionId: intent.Id,
                FailureReason: $"Capture unexpected status: {intent.Status}",
                IsRetriable: false);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error during Stripe capture");
            return new GatewayResult(false, null, "Network error", IsRetriable: true);
        }
    }

    /// <summary>
    /// Captures a previously authorized payment.
    /// Maps to: POST /v1/payment_intents/{id}/capture
    /// </summary>
    public async Task<GatewayResult> CaptureAuthorizedAsync(
        string authorizationId,
        decimal amount,
        CancellationToken ct)
    {
        // Use authorizationId in idempotency key (deterministic for same capture)
        var idempotencyKey = $"capture-{authorizationId}";

        try
        {
            var payload = new Dictionary<string, string>
            {
                ["amount_to_capture"] = ConvertToStripeAmount(amount, "usd").ToString()
            };

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"/v1/payment_intents/{authorizationId}/capture");
            request.Headers.Add("Idempotency-Key", idempotencyKey);
            request.Content = new FormUrlEncodedContent(payload);

            var response = await _httpClient.SendAsync(request, ct);
            var content = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                var error = JsonSerializer.Deserialize<StripeErrorResponse>(content);
                return MapStripeError(error);
            }

            var intent = JsonSerializer.Deserialize<StripePaymentIntentResponse>(content);

            if (intent.Status == "succeeded")
            {
                var chargeId = intent.LatestCharge ?? intent.Id;

                _logger.LogInformation(
                    "Authorized payment captured: {ChargeId} from {AuthId}",
                    chargeId, authorizationId);

                return new GatewayResult(
                    Success: true,
                    TransactionId: chargeId,
                    FailureReason: null,
                    IsRetriable: false);
            }

            return new GatewayResult(
                Success: false,
                TransactionId: intent.Id,
                FailureReason: $"Capture failed: {intent.Status}",
                IsRetriable: false);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error during Stripe authorized capture");
            return new GatewayResult(false, null, "Network error", IsRetriable: true);
        }
    }

    /// <summary>
    /// Refunds a previously captured payment.
    /// Maps to: POST /v1/refunds
    /// </summary>
    public async Task<GatewayResult> RefundAsync(
        string transactionId,
        decimal amount,
        CancellationToken ct)
    {
        // Include amount in idempotency key (multiple partial refunds possible)
        var idempotencyKey = $"refund-{transactionId}-{amount:0.00}";

        try
        {
            var payload = new Dictionary<string, string>
            {
                ["charge"] = transactionId,
                ["amount"] = ConvertToStripeAmount(amount, "usd").ToString()
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/refunds");
            request.Headers.Add("Idempotency-Key", idempotencyKey);
            request.Content = new FormUrlEncodedContent(payload);

            var response = await _httpClient.SendAsync(request, ct);
            var content = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                var error = JsonSerializer.Deserialize<StripeErrorResponse>(content);
                return MapStripeError(error);
            }

            var refund = JsonSerializer.Deserialize<StripeRefundResponse>(content);

            if (refund.Status == "succeeded")
            {
                _logger.LogInformation(
                    "Refund succeeded: {RefundId} for {Amount} on {ChargeId}",
                    refund.Id, amount, transactionId);

                return new GatewayResult(
                    Success: true,
                    TransactionId: refund.Id,
                    FailureReason: null,
                    IsRetriable: false);
            }

            // Pending refunds may succeed later (check via webhook)
            var isRetriable = refund.Status == "pending";

            return new GatewayResult(
                Success: false,
                TransactionId: refund.Id,
                FailureReason: $"Refund {refund.Status}",
                IsRetriable: isRetriable);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error during Stripe refund");
            return new GatewayResult(false, null, "Network error", IsRetriable: true);
        }
    }

    /// <summary>
    /// Converts decimal amount to Stripe's integer format (smallest currency unit).
    /// Example: $19.99 USD = 1999 cents
    /// </summary>
    private static long ConvertToStripeAmount(decimal amount, string currency)
    {
        // Zero-decimal currencies (JPY, KRW) don't multiply by 100
        var zeroDecimalCurrencies = new[] { "jpy", "krw", "vnd", "clp" };

        if (zeroDecimalCurrencies.Contains(currency.ToLowerInvariant()))
        {
            return (long)amount;
        }

        // Most currencies use 2 decimal places (cents)
        return (long)(amount * 100);
    }

    /// <summary>
    /// Maps Stripe error responses to GatewayResult.
    /// Determines if error is retriable based on error type.
    /// </summary>
    private static GatewayResult MapStripeError(StripeErrorResponse? errorResponse)
    {
        if (errorResponse?.Error == null)
        {
            return new GatewayResult(false, null, "Unknown error", IsRetriable: false);
        }

        var error = errorResponse.Error;

        // Determine retriability based on error type
        var isRetriable = error.Type switch
        {
            "api_error" => true,           // Stripe internal error (rare)
            "rate_limit_error" => true,    // Too many requests (429)
            "card_error" => false,         // Card declined (user must fix)
            "invalid_request_error" => false,  // Bad request (our bug)
            "authentication_error" => false,   // Invalid API key (config issue)
            _ => false
        };

        // Some card errors are retriable (processing_error)
        if (error.Type == "card_error" && error.DeclineCode == "processing_error")
        {
            isRetriable = true;
        }

        return new GatewayResult(
            Success: false,
            TransactionId: null,
            FailureReason: error.Message ?? "Unknown error",
            IsRetriable: isRetriable);
    }
}

// -----------------------------------------------------------------------------
// Stripe API Response Models
// -----------------------------------------------------------------------------

internal sealed record StripePaymentIntentResponse(
    string Id,
    string Status,
    long Amount,
    string Currency,
    string? LatestCharge);

internal sealed record StripeRefundResponse(
    string Id,
    string Status,
    long Amount,
    string Charge);

internal sealed record StripeErrorResponse(
    StripeError Error);

internal sealed record StripeError(
    string Type,
    string? Code,
    string? Message,
    string? DeclineCode);
