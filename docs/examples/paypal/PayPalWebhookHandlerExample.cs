// =============================================================================
// PayPalWebhookHandlerExample.cs
// =============================================================================
// IMPORTANT: This is a REFERENCE EXAMPLE — not production-ready code.
// Use this to understand PayPal's webhook verification patterns before building
// the real webhook handler in src/Payments/Payments.Api/Webhooks/.
//
// PayPal webhook security differs from Stripe in these important ways:
//   1. ASYMMETRIC (RSA-SHA256) instead of Stripe's symmetric (HMAC-SHA256)
//   2. Signed message includes CRC32 checksum of body (decimal int, not full hash)
//   3. Public key must be DOWNLOADED from paypal-cert-url header — validate the URL!
//   4. SSRF vulnerability if certUrl is not validated before downloading
//   5. No timestamp tolerance check (PayPal uses certUrl + transmissionId instead)
//
// Retry policy: PayPal retries up to 25 times over 3 days (similar to Stripe's 3 days).
// =============================================================================

using System.IO.Hashing;
using System.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Wolverine.Http;

namespace Payments.Processing.PayPal.Examples;

// =============================================================================
// Webhook Event DTOs
// =============================================================================

/// <summary>
/// Top-level PayPal webhook event envelope.
/// All PayPal webhook events share this structure; the resource object varies
/// depending on the event_type.
/// </summary>
public sealed class PayPalWebhookEvent
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = default!;          // Unique webhook event ID (use for deduplication)

    [JsonPropertyName("event_type")]
    public string EventType { get; set; } = default!;   // e.g., "PAYMENT.CAPTURE.COMPLETED"

    [JsonPropertyName("event_version")]
    public string? EventVersion { get; set; }

    [JsonPropertyName("resource_type")]
    public string? ResourceType { get; set; }           // e.g., "capture", "refund"

    [JsonPropertyName("resource_version")]
    public string? ResourceVersion { get; set; }

    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    [JsonPropertyName("create_time")]
    public DateTimeOffset CreateTime { get; set; }

    [JsonPropertyName("resource")]
    public PayPalWebhookResource? Resource { get; set; }
}

/// <summary>
/// The resource object within a PayPal webhook event.
/// The relevant fields vary by event_type; this covers the common payment events.
/// </summary>
public sealed class PayPalWebhookResource
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }                     // Capture ID or Authorization ID

    [JsonPropertyName("status")]
    public string? Status { get; set; }                 // "COMPLETED", "DECLINED", etc.

    [JsonPropertyName("amount")]
    public PayPalAmount? Amount { get; set; }

    [JsonPropertyName("supplementary_data")]
    public PayPalSupplementaryData? SupplementaryData { get; set; }

    [JsonPropertyName("custom_id")]
    public string? CustomId { get; set; }               // Merchant-supplied reference (optional)
}

public sealed class PayPalAmount
{
    [JsonPropertyName("currency_code")]
    public string CurrencyCode { get; set; } = default!;

    [JsonPropertyName("value")]
    public string Value { get; set; } = default!;       // Decimal string ("19.99") — NOT cents
}

public sealed class PayPalSupplementaryData
{
    [JsonPropertyName("related_ids")]
    public PayPalRelatedIds? RelatedIds { get; set; }
}

public sealed class PayPalRelatedIds
{
    [JsonPropertyName("order_id")]
    public string? OrderId { get; set; }        // Links back to the PayPal Order ID
    
    [JsonPropertyName("authorization_id")]
    public string? AuthorizationId { get; set; }

    [JsonPropertyName("capture_id")]
    public string? CaptureId { get; set; }
}

// =============================================================================
// Webhook Handler
// =============================================================================

/// <summary>
/// Handles PayPal webhook events for the Payments BC.
///
/// Endpoint: POST /api/webhooks/paypal
///
/// Webhook events handled:
///   - PAYMENT.CAPTURE.COMPLETED  → Publish PaymentCaptured integration message
///   - PAYMENT.CAPTURE.DECLINED   → Publish PaymentFailed integration message
///   - PAYMENT.CAPTURE.REFUNDED   → Publish RefundCompleted integration message
///   - PAYMENT.REFUND.FAILED      → Publish RefundFailed integration message
///
/// Security: RSA-SHA256 signature verification (see WEBHOOK-SECURITY.md)
/// Idempotency: Webhook event IDs stored in Marten to prevent duplicate processing
/// </summary>
public static class PayPalWebhookHandlerExample
{
    /// <summary>
    /// Wolverine HTTP endpoint for PayPal webhook delivery.
    ///
    /// IMPORTANT: Must read body as raw bytes BEFORE any JSON parsing,
    /// because the CRC32 checksum must be computed on the ORIGINAL raw bytes.
    /// Parsing to JSON and re-serializing changes whitespace and may alter the CRC32.
    /// </summary>
    [WolverinePost("/api/webhooks/paypal")]
    public static async Task<IResult> HandleWebhook(
        HttpRequest request,
        PayPalWebhookVerifier verifier,
        PayPalWebhookProcessor processor,
        ILogger<PayPalWebhookEvent> logger,
        CancellationToken cancellationToken)
    {
        // 1. Read raw body bytes (needed for CRC32 computation)
        //    ⚠️ Must be done BEFORE reading as string — both reads consume the stream
        using var bodyStream = new MemoryStream();
        await request.Body.CopyToAsync(bodyStream, cancellationToken);
        var rawBodyBytes = bodyStream.ToArray();
        var rawBodyString = Encoding.UTF8.GetString(rawBodyBytes);

        // 2. Extract PayPal signature headers
        var transmissionId = request.Headers["paypal-transmission-id"].ToString();
        var transmissionTime = request.Headers["paypal-transmission-time"].ToString();
        var certUrl = request.Headers["paypal-cert-url"].ToString();
        var transmissionSig = request.Headers["paypal-transmission-sig"].ToString();

        if (string.IsNullOrEmpty(transmissionId) || string.IsNullOrEmpty(certUrl) || string.IsNullOrEmpty(transmissionSig))
        {
            logger.LogWarning("PayPal webhook missing required headers");
            return Results.BadRequest("Missing required PayPal signature headers");
        }

        // 3. Verify the webhook signature
        bool isSignatureValid;
        try
        {
            isSignatureValid = await verifier.VerifyAsync(
                rawBodyBytes, transmissionId, transmissionTime, certUrl, transmissionSig, cancellationToken);
        }
        catch (SecurityException ex)
        {
            logger.LogError(ex, "PayPal webhook cert URL validation failed: {CertUrl}", certUrl);
            return Results.Unauthorized();
        }

        if (!isSignatureValid)
        {
            logger.LogWarning("PayPal webhook signature verification failed: {TransmissionId}", transmissionId);
            return Results.Unauthorized();
        }

        // 4. Parse the event
        PayPalWebhookEvent? webhookEvent;
        try
        {
            webhookEvent = JsonSerializer.Deserialize<PayPalWebhookEvent>(rawBodyString);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "PayPal webhook JSON parse failed");
            return Results.BadRequest("Invalid webhook payload");
        }

        if (webhookEvent is null)
        {
            logger.LogWarning("PayPal webhook deserialized to null");
            return Results.BadRequest("Empty webhook payload");
        }

        // 5. Process the event (handles deduplication internally)
        try
        {
            await processor.ProcessAsync(webhookEvent, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "PayPal webhook processing failed: {EventId} {EventType}",
                webhookEvent.Id, webhookEvent.EventType);

            // ⚠️ Return 500 so PayPal retries this webhook (up to 25 times over 3 days)
            // Only return 200 once the event is successfully processed
            return Results.StatusCode(500);
        }

        // 6. Return 200 OK — tells PayPal delivery was successful
        return Results.Ok();
    }
}

// =============================================================================
// Signature Verifier
// =============================================================================

/// <summary>
/// Verifies PayPal webhook signatures using RSA-SHA256 + CRC32.
///
/// PayPal's verification algorithm:
///   1. Compute CRC32 of raw body (decimal int)
///   2. Build message: "{transmissionId}|{timeStamp}|{webhookId}|{crc32Decimal}"
///   3. Download (and cache) certificate from paypal-cert-url
///   4. Verify RSA-SHA256 signature using certificate's public key
/// </summary>
public sealed class PayPalWebhookVerifier
{
    private readonly IMemoryCache _certCache;
    private readonly HttpClient _httpClient;
    private readonly string _webhookId;
    private readonly ILogger<PayPalWebhookVerifier> _logger;

    public PayPalWebhookVerifier(
        IMemoryCache certCache,
        HttpClient httpClient,
        Microsoft.Extensions.Configuration.IConfiguration configuration,
        ILogger<PayPalWebhookVerifier> logger)
    {
        _certCache = certCache;
        _httpClient = httpClient;
        _logger = logger;
        _webhookId = configuration["PayPal:WebhookId"]
            ?? throw new InvalidOperationException("PayPal:WebhookId not configured");
    }

    public async Task<bool> VerifyAsync(
        byte[] rawBody,
        string transmissionId,
        string transmissionTime,
        string certUrl,
        string transmissionSig,
        CancellationToken ct)
    {
        // 1. Compute CRC32 checksum of raw body (expressed as decimal integer)
        //    ⚠️ Use System.IO.Hashing.Crc32 (.NET 6+), not legacy CRC32 implementations
        var crc32Value = Crc32.HashToUInt32(rawBody);

        // 2. Construct the signed message string
        //    Format: "{transmissionId}|{transmissionTime}|{webhookId}|{crc32Decimal}"
        var message = $"{transmissionId}|{transmissionTime}|{_webhookId}|{crc32Value}";
        var messageBytes = Encoding.UTF8.GetBytes(message);

        _logger.LogDebug("PayPal webhook signed message: {Message}", message);

        // 3. Download and cache the PayPal signing certificate
        var certPem = await GetCachedCertificateAsync(certUrl, ct);

        // 4. Extract RSA public key from X.509 certificate
        using var cert = X509Certificate2.CreateFromPem(certPem);
        using var rsa = cert.GetRSAPublicKey();

        if (rsa is null)
        {
            _logger.LogError("PayPal certificate has no RSA public key: {CertUrl}", certUrl);
            return false;
        }

        // 5. Decode and verify the Base64-encoded RSA-SHA256 signature
        var signatureBytes = Convert.FromBase64String(transmissionSig);

        return rsa.VerifyData(
            messageBytes,
            signatureBytes,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
    }

    private async Task<string> GetCachedCertificateAsync(string certUrl, CancellationToken ct)
    {
        // ⚠️ SSRF PROTECTION: Validate certUrl domain before downloading
        // An attacker could forge the header to point to an internal URL
        if (!certUrl.StartsWith("https://api.paypal.com/", StringComparison.OrdinalIgnoreCase) &&
            !certUrl.StartsWith("https://api.sandbox.paypal.com/", StringComparison.OrdinalIgnoreCase))
        {
            throw new SecurityException($"PayPal cert URL domain not trusted: {certUrl}");
        }

        // Cache by URL — PayPal certs rotate, new cert = new URL = cache miss (correct behavior)
        var cacheKey = $"paypal-cert-{certUrl.GetHashCode()}";

        if (_certCache.TryGetValue(cacheKey, out string? cachedCert) && cachedCert is not null)
        {
            return cachedCert;
        }

        _logger.LogInformation("Downloading PayPal certificate: {CertUrl}", certUrl);
        var pem = await _httpClient.GetStringAsync(certUrl, ct);

        // Cache for 24 hours — PayPal certs are long-lived
        _certCache.Set(cacheKey, pem, TimeSpan.FromHours(24));

        return pem;
    }
}

// =============================================================================
// Event Processor
// =============================================================================

/// <summary>
/// Routes PayPal webhook events to the appropriate handler and publishes
/// integration messages to the rest of CritterSupply's event-driven system.
/// </summary>
public sealed class PayPalWebhookProcessor
{
    private readonly Marten.IDocumentSession _session;
    private readonly Wolverine.IMessageBus _bus;
    private readonly ILogger<PayPalWebhookProcessor> _logger;

    public PayPalWebhookProcessor(
        Marten.IDocumentSession session,
        Wolverine.IMessageBus bus,
        ILogger<PayPalWebhookProcessor> logger)
    {
        _session = session;
        _bus = bus;
        _logger = logger;
    }

    public async Task ProcessAsync(PayPalWebhookEvent webhookEvent, CancellationToken ct)
    {
        // Idempotency check: have we already processed this webhook event?
        var alreadyProcessed = await _session.LoadAsync<ProcessedPayPalWebhookEvent>(webhookEvent.Id, ct);
        if (alreadyProcessed is not null)
        {
            _logger.LogInformation("PayPal webhook already processed (idempotent): {EventId}", webhookEvent.Id);
            return;
        }

        _logger.LogInformation("Processing PayPal webhook: {EventId} {EventType}", webhookEvent.Id, webhookEvent.EventType);

        switch (webhookEvent.EventType)
        {
            case "PAYMENT.CAPTURE.COMPLETED":
                await HandleCaptureCompletedAsync(webhookEvent, ct);
                break;

            case "PAYMENT.CAPTURE.DECLINED":
                await HandleCaptureDeclinedAsync(webhookEvent, ct);
                break;

            case "PAYMENT.CAPTURE.PENDING":
                // Not a terminal state — log and wait for COMPLETED or DECLINED
                _logger.LogInformation("PayPal capture PENDING (waiting for completion): {EventId}", webhookEvent.Id);
                break;

            case "PAYMENT.CAPTURE.REFUNDED":
                await HandleCaptureRefundedAsync(webhookEvent, ct);
                break;

            case "PAYMENT.REFUND.FAILED":
                await HandleRefundFailedAsync(webhookEvent, ct);
                break;

            case "PAYMENT.AUTHORIZATION.VOIDED":
                _logger.LogInformation("PayPal authorization voided: {EventId}", webhookEvent.Id);
                // Internal: authorization was voided (by us or expired)
                break;

            case "CUSTOMER.DISPUTE.CREATED":
                _logger.LogWarning("PayPal dispute opened — manual review required: {EventId}", webhookEvent.Id);
                // TODO: Publish dispute notification in future cycle (Notifications BC)
                break;

            default:
                _logger.LogDebug("PayPal webhook event type not handled: {EventType}", webhookEvent.EventType);
                break;
        }

        // Store processed event ID for deduplication
        var processed = new ProcessedPayPalWebhookEvent(webhookEvent.Id, webhookEvent.EventType, DateTimeOffset.UtcNow);
        _session.Store(processed);
        await _session.SaveChangesAsync(ct);
    }

    private async Task HandleCaptureCompletedAsync(PayPalWebhookEvent webhookEvent, CancellationToken ct)
    {
        var resource = webhookEvent.Resource;
        var captureId = resource?.Id;
        var orderIdFromEvent = resource?.SupplementaryData?.RelatedIds?.OrderId;

        if (captureId is null)
        {
            _logger.LogError("PayPal PAYMENT.CAPTURE.COMPLETED missing capture ID: {EventId}", webhookEvent.Id);
            return;
        }

        // Look up our internal payment record by PayPal orderID or captureID
        // In a real implementation, query the Payments event stream
        // The Payments BC stores the PayPal orderID when the payment was initiated

        _logger.LogInformation("PayPal capture completed: CaptureId={CaptureId} OrderId={OrderId}",
            captureId, orderIdFromEvent);

        // Note: In most cases, the Payments BC has already processed this via the HTTP response
        // from CaptureAsync(). This webhook handler handles the case where that HTTP response
        // was lost (network failure), ensuring eventual delivery via webhook.
        //
        // The handler must be idempotent — check if PaymentCaptured was already published.
    }

    private async Task HandleCaptureDeclinedAsync(PayPalWebhookEvent webhookEvent, CancellationToken ct)
    {
        var resource = webhookEvent.Resource;
        var captureId = resource?.Id;

        _logger.LogWarning("PayPal capture DECLINED: CaptureId={CaptureId} EventId={EventId}",
            captureId, webhookEvent.Id);

        // Publish PaymentFailed integration message to Orders saga
        // Orders saga will decide: retry (IsRetriable=true) or cancel order (IsRetriable=false)
        // PayPal declined = non-retriable (buyer declined or insufficient funds)
    }

    private async Task HandleCaptureRefundedAsync(PayPalWebhookEvent webhookEvent, CancellationToken ct)
    {
        var resource = webhookEvent.Resource;
        var captureId = resource?.SupplementaryData?.RelatedIds?.CaptureId;
        var amount = decimal.TryParse(resource?.Amount?.Value, out var parsed) ? parsed : 0m;

        _logger.LogInformation("PayPal capture refunded: CaptureId={CaptureId} Amount={Amount} EventId={EventId}",
            captureId, amount, webhookEvent.Id);

        // Publish RefundCompleted integration message to Orders saga
    }

    private async Task HandleRefundFailedAsync(PayPalWebhookEvent webhookEvent, CancellationToken ct)
    {
        _logger.LogError("PayPal refund FAILED — manual intervention required: EventId={EventId}", webhookEvent.Id);

        // Publish RefundFailed integration message to Orders saga
        // Orders saga will escalate to manual review
    }
}

// =============================================================================
// Marten Document: Processed Event Deduplication
// =============================================================================

/// <summary>
/// Marten document tracking processed PayPal webhook events.
/// Prevents duplicate processing when PayPal retries webhook delivery.
///
/// PayPal retries webhooks up to 25 times over 3 days when your endpoint
/// returns a non-2xx response or doesn't respond at all.
/// </summary>
public sealed record ProcessedPayPalWebhookEvent(
    string Id,          // PayPal webhook event ID (e.g., "WH-2WR32451HC0233532-67976317FL4543714")
    string EventType,   // e.g., "PAYMENT.CAPTURE.COMPLETED"
    DateTimeOffset ProcessedAt);
