// Example Stripe Webhook Handler
// Location: docs/examples/stripe/StripeWebhookHandlerExample.cs
// Purpose: Reference implementation for handling Stripe webhook events

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace Payments.Api.Webhooks.Examples;

/// <summary>
/// Example implementation of Stripe webhook handler.
/// Demonstrates key patterns for webhook integration:
/// - HMAC-SHA256 signature verification (prevents spoofing)
/// - Event deduplication (Stripe may send duplicates)
/// - Idempotent message publishing (safe to reprocess)
/// - Correlation via metadata (linking Stripe events to CritterSupply aggregates)
/// </summary>
public static class StripeWebhookHandlerExample
{
    /// <summary>
    /// Webhook endpoint that receives events from Stripe.
    /// URL: POST /api/webhooks/stripe
    /// 
    /// Stripe sends JSON payload with signature header for verification.
    /// </summary>
    [WolverinePost("/api/webhooks/stripe")]
    public static async Task<IResult> Handle(
        HttpRequest httpRequest,  // Need raw request for signature verification
        IConfiguration configuration,
        IMessageBus messageBus,
        IDocumentSession session,  // For event deduplication
        ILogger logger,
        CancellationToken ct)
    {
        // Read raw body (needed for signature verification)
        using var reader = new StreamReader(httpRequest.Body);
        var payload = await reader.ReadToEndAsync(ct);

        // Get Stripe signature from header
        if (!httpRequest.Headers.TryGetValue("Stripe-Signature", out var signatureHeader))
        {
            logger.LogWarning("Webhook received without Stripe-Signature header");
            return Results.BadRequest("Missing signature");
        }

        // Verify webhook authenticity
        var webhookSecret = configuration["Stripe:WebhookSecret"]
            ?? throw new InvalidOperationException("Stripe:WebhookSecret not configured");

        if (!VerifyWebhookSignature(payload, signatureHeader!, webhookSecret))
        {
            logger.LogWarning("Webhook signature verification failed");
            return Results.Unauthorized();
        }

        // Deserialize event
        var webhookEvent = JsonSerializer.Deserialize<StripeWebhookEvent>(payload);
        if (webhookEvent == null)
        {
            logger.LogWarning("Failed to deserialize webhook event");
            return Results.BadRequest("Invalid event payload");
        }

        // Check for duplicate event (Stripe may retry)
        var existingEvent = await session.Query<ProcessedWebhookEvent>()
            .FirstOrDefaultAsync(e => e.StripeEventId == webhookEvent.Id, ct);

        if (existingEvent != null)
        {
            logger.LogInformation(
                "Webhook event {EventId} already processed at {ProcessedAt}",
                webhookEvent.Id, existingEvent.ProcessedAt);
            return Results.Ok(); // Return 200 OK (already processed, idempotent)
        }

        logger.LogInformation(
            "Processing Stripe webhook: {EventType} for {ObjectId}",
            webhookEvent.Type,
            webhookEvent.Data.Object.Id);

        // Route event to appropriate handler
        switch (webhookEvent.Type)
        {
            case "payment_intent.succeeded":
                await HandlePaymentSucceeded(webhookEvent, messageBus, logger, ct);
                break;

            case "payment_intent.payment_failed":
                await HandlePaymentFailed(webhookEvent, messageBus, logger, ct);
                break;

            case "charge.refunded":
                await HandleChargeRefunded(webhookEvent, messageBus, logger, ct);
                break;

            default:
                logger.LogInformation("Unhandled webhook event type: {EventType}", webhookEvent.Type);
                break;
        }

        // Store event ID for deduplication
        session.Store(new ProcessedWebhookEvent(
            webhookEvent.Id,
            webhookEvent.Type,
            DateTimeOffset.UtcNow));

        await session.SaveChangesAsync(ct);

        return Results.Ok();
    }

    /// <summary>
    /// Handles successful payment completion.
    /// Maps to: payment_intent.succeeded webhook event
    /// </summary>
    private static async Task HandlePaymentSucceeded(
        StripeWebhookEvent webhookEvent,
        IMessageBus messageBus,
        ILogger logger,
        CancellationToken ct)
    {
        var intent = webhookEvent.Data.Object;

        // Extract CritterSupply payment ID from Stripe metadata
        if (!intent.Metadata.TryGetValue("payment_id", out var paymentIdStr) ||
            !Guid.TryParse(paymentIdStr, out var paymentId))
        {
            logger.LogWarning(
                "PaymentIntent {IntentId} missing payment_id in metadata",
                intent.Id);
            return;
        }

        // Extract order ID (optional, for logging)
        intent.Metadata.TryGetValue("order_id", out var orderIdStr);

        logger.LogInformation(
            "Payment succeeded: {PaymentId} (Stripe: {IntentId}, Order: {OrderId})",
            paymentId, intent.Id, orderIdStr);

        // Publish internal integration event
        // This triggers the Order saga to continue
        await messageBus.PublishAsync(
            new Messages.Contracts.Payments.PaymentCaptured(
                paymentId,
                Guid.Parse(orderIdStr!),
                intent.Amount / 100m,  // Convert cents to dollars
                intent.Id,
                DateTimeOffset.UtcNow),
            ct);
    }

    /// <summary>
    /// Handles payment failure.
    /// Maps to: payment_intent.payment_failed webhook event
    /// </summary>
    private static async Task HandlePaymentFailed(
        StripeWebhookEvent webhookEvent,
        IMessageBus messageBus,
        ILogger logger,
        CancellationToken ct)
    {
        var intent = webhookEvent.Data.Object;

        if (!intent.Metadata.TryGetValue("payment_id", out var paymentIdStr) ||
            !Guid.TryParse(paymentIdStr, out var paymentId))
        {
            logger.LogWarning(
                "PaymentIntent {IntentId} missing payment_id in metadata",
                intent.Id);
            return;
        }

        intent.Metadata.TryGetValue("order_id", out var orderIdStr);

        var failureMessage = intent.LastPaymentError?.Message ?? "Payment failed";
        var declineCode = intent.LastPaymentError?.DeclineCode;

        logger.LogWarning(
            "Payment failed: {PaymentId} (Stripe: {IntentId}, Reason: {Reason}, DeclineCode: {DeclineCode})",
            paymentId, intent.Id, failureMessage, declineCode);

        // Determine if retriable based on decline code
        var isRetriable = declineCode == "processing_error";

        await messageBus.PublishAsync(
            new Messages.Contracts.Payments.PaymentFailed(
                paymentId,
                Guid.Parse(orderIdStr!),
                failureMessage,
                isRetriable,
                DateTimeOffset.UtcNow),
            ct);
    }

    /// <summary>
    /// Handles refund completion.
    /// Maps to: charge.refunded webhook event
    /// </summary>
    private static async Task HandleChargeRefunded(
        StripeWebhookEvent webhookEvent,
        IMessageBus messageBus,
        ILogger logger,
        CancellationToken ct)
    {
        // In production, extract refund ID from metadata and publish RefundCompleted
        logger.LogInformation(
            "Refund webhook received for charge: {ChargeId}",
            webhookEvent.Data.Object.Id);

        // Example: await messageBus.PublishAsync(new RefundCompleted(...), ct);
    }

    /// <summary>
    /// Verifies webhook signature using HMAC-SHA256.
    /// This prevents malicious actors from spoofing Stripe webhooks.
    /// 
    /// Stripe-Signature header format: t=timestamp,v1=signature
    /// Signed payload format: {timestamp}.{rawBody}
    /// </summary>
    private static bool VerifyWebhookSignature(
        string payload,
        string signatureHeader,
        string webhookSecret)
    {
        try
        {
            // Parse signature header (format: t=1234567890,v1=abc123...)
            var elements = signatureHeader.Split(',');
            var timestamp = "";
            var signature = "";

            foreach (var element in elements)
            {
                var parts = element.Split('=', 2);
                if (parts.Length != 2) continue;

                if (parts[0] == "t")
                    timestamp = parts[1];
                else if (parts[0] == "v1")
                    signature = parts[1];
            }

            if (string.IsNullOrEmpty(timestamp) || string.IsNullOrEmpty(signature))
            {
                return false;
            }

            // Check timestamp tolerance (prevent replay attacks)
            var eventTimestamp = DateTimeOffset.FromUnixTimeSeconds(long.Parse(timestamp));
            var age = DateTimeOffset.UtcNow - eventTimestamp;

            if (age > TimeSpan.FromMinutes(5))
            {
                // Event too old (possible replay attack)
                return false;
            }

            // Construct signed payload: {timestamp}.{rawBody}
            var signedPayload = $"{timestamp}.{payload}";

            // Compute HMAC-SHA256 signature
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(webhookSecret));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(signedPayload));
            var computedSignature = BitConverter.ToString(hash)
                .Replace("-", "")
                .ToLowerInvariant();

            // Constant-time comparison (prevents timing attacks)
            return CryptographicEquals(signature, computedSignature);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Constant-time string comparison to prevent timing attacks.
    /// </summary>
    private static bool CryptographicEquals(string a, string b)
    {
        if (a.Length != b.Length)
            return false;

        var areEqual = true;
        for (var i = 0; i < a.Length; i++)
        {
            areEqual &= a[i] == b[i];
        }

        return areEqual;
    }
}

// -----------------------------------------------------------------------------
// Webhook Event Models
// -----------------------------------------------------------------------------

/// <summary>
/// Stripe webhook event structure.
/// All webhook events follow this format.
/// </summary>
internal sealed record StripeWebhookEvent(
    string Id,              // evt_xxx
    string Type,            // "payment_intent.succeeded", etc.
    long Created,           // Unix timestamp
    StripeEventData Data);

internal sealed record StripeEventData(
    StripePaymentIntentWebhookObject Object);

/// <summary>
/// Simplified PaymentIntent structure for webhook events.
/// Only includes fields relevant to webhook handling.
/// </summary>
internal sealed record StripePaymentIntentWebhookObject(
    string Id,              // pi_xxx or ch_xxx
    string Status,          // "succeeded", "failed", etc.
    long Amount,            // Amount in cents
    string Currency,        // "usd", "eur", etc.
    Dictionary<string, string> Metadata,  // Custom data (payment_id, order_id)
    StripePaymentError? LastPaymentError);

internal sealed record StripePaymentError(
    string? Message,
    string? DeclineCode);

/// <summary>
/// Document stored in Marten to track processed webhook events.
/// Prevents duplicate processing if Stripe resends events.
/// </summary>
public sealed record ProcessedWebhookEvent(
    string StripeEventId,
    string EventType,
    DateTimeOffset ProcessedAt)
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
}
