// =============================================================================
// ShopifyWebhookHandlerExample.cs
// Reference implementation for handling Shopify webhook events
//
// PURPOSE: Demonstrates production-ready patterns for:
//   - HMAC-SHA256 webhook signature verification (security)
//   - Webhook deduplication via X-Shopify-Event-Id header
//   - Order ingestion (orders/create webhook)
//   - Order cancellation handling (orders/cancelled webhook)
//   - Fulfillment status updates (fulfillments/update webhook)
//   - GDPR mandatory webhooks (customers/data_request, customers/redact, shop/redact)
//   - 5-second response deadline compliance (async processing via Wolverine)
//
// IMPORTANT: This file is a REFERENCE EXAMPLE only.
// Real implementation goes in src/Marketplaces/Marketplaces.Api/ (Cycle 35).
//
// Integration notes:
// - Webhooks are at-least-once — duplicate delivery is normal
// - No guaranteed ordering — use timestamps for conflict resolution
// - Respond 200 OK within 5 seconds; process asynchronously via RabbitMQ
// - HMAC must be verified from RAW bytes before JSON deserialization
// =============================================================================

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace Marketplaces.Api.Webhooks;

// ---------------------------------------------------------------------------
// Webhook Request Headers Model
// ---------------------------------------------------------------------------

/// <summary>
/// Headers present on every Shopify webhook delivery.
/// Header names are case-insensitive in HTTP — treat all casings as equivalent.
/// </summary>
public sealed record ShopifyWebhookHeaders
{
    /// <summary>The webhook topic, e.g., "orders/create".</summary>
    public string Topic { get; init; } = default!;

    /// <summary>
    /// Base64-encoded HMAC-SHA256 of the raw request body.
    /// Use this to verify the webhook originated from Shopify.
    /// </summary>
    public string HmacSha256 { get; init; } = default!;

    /// <summary>The store domain that sent this webhook.</summary>
    public string ShopDomain { get; init; } = default!;

    /// <summary>Unique ID for this webhook delivery attempt.</summary>
    public string WebhookId { get; init; } = default!;

    /// <summary>
    /// Stable event identifier. Use this for deduplication.
    /// The same event ID is preserved across retry attempts.
    /// </summary>
    public string EventId { get; init; } = default!;

    /// <summary>When Shopify triggered the webhook (not when it was delivered).</summary>
    public DateTimeOffset TriggeredAt { get; init; }
}

// ---------------------------------------------------------------------------
// Order Payload Models
// ---------------------------------------------------------------------------

/// <summary>
/// Shopify order payload received via orders/create webhook.
///
/// Key architectural insight: financialStatus is already PAID when we receive this.
/// Shopify captures payment at checkout — the Orders saga must handle Shopify orders
/// via PlaceMarketplaceOrder (Option A from Payment Seam ADR), bypassing Payments BC.
///
/// Store shopifyOrderId as correlation ID on the CritterSupply Order aggregate,
/// using the NUMERIC portion of the GID (not the human-readable #1001 "name" field).
/// The "name" field (#1001) is NOT globally unique and NOT suitable for correlation.
/// </summary>
public sealed record ShopifyOrderPayload
{
    [JsonPropertyName("id")]
    public long Id { get; init; }  // Numeric Shopify order ID (stable, unique)

    [JsonPropertyName("admin_graphql_api_id")]
    public string AdminGraphQlApiId { get; init; } = default!;  // gid://shopify/Order/{id}

    [JsonPropertyName("name")]
    public string Name { get; init; } = default!;  // "#1001" — human-readable, NOT a correlation key

    [JsonPropertyName("email")]
    public string? Email { get; init; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("financial_status")]
    public string FinancialStatus { get; init; } = default!;  // "paid" for all normal Shopify orders

    [JsonPropertyName("fulfillment_status")]
    public string? FulfillmentStatus { get; init; }  // null = unfulfilled

    [JsonPropertyName("total_price")]
    public string TotalPrice { get; init; } = default!;

    [JsonPropertyName("currency")]
    public string Currency { get; init; } = default!;

    [JsonPropertyName("line_items")]
    public IReadOnlyList<ShopifyLineItem> LineItems { get; init; } = [];

    [JsonPropertyName("shipping_address")]
    public ShopifyAddress? ShippingAddress { get; init; }

    [JsonPropertyName("billing_address")]
    public ShopifyAddress? BillingAddress { get; init; }

    [JsonPropertyName("transactions")]
    public IReadOnlyList<ShopifyTransaction> Transactions { get; init; } = [];

    // Store this on the CritterSupply Order aggregate for reconciliation
    // This is the Shopify Payments payout identifier (per PO feedback)
    public string? ShopifyTransactionId => Transactions.FirstOrDefault()?.Id.ToString();
}

public sealed record ShopifyLineItem
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("title")]
    public string Title { get; init; } = default!;

    [JsonPropertyName("quantity")]
    public int Quantity { get; init; }

    [JsonPropertyName("sku")]
    public string? Sku { get; init; }  // Maps to CritterSupply ProductVariant.Sku

    [JsonPropertyName("variant_id")]
    public long? VariantId { get; init; }  // Shopify ProductVariant ID

    [JsonPropertyName("product_id")]
    public long? ProductId { get; init; }  // Shopify Product ID

    [JsonPropertyName("price")]
    public string Price { get; init; } = default!;

    [JsonPropertyName("vendor")]
    public string? Vendor { get; init; }

    [JsonPropertyName("requires_shipping")]
    public bool RequiresShipping { get; init; }

    [JsonPropertyName("fulfillment_service")]
    public string? FulfillmentService { get; init; }  // "manual" = fulfilled by merchant
}

public sealed record ShopifyAddress
{
    [JsonPropertyName("first_name")]
    public string? FirstName { get; init; }

    [JsonPropertyName("last_name")]
    public string? LastName { get; init; }

    [JsonPropertyName("address1")]
    public string? Address1 { get; init; }

    [JsonPropertyName("address2")]
    public string? Address2 { get; init; }

    [JsonPropertyName("city")]
    public string? City { get; init; }

    [JsonPropertyName("province")]
    public string? Province { get; init; }

    [JsonPropertyName("zip")]
    public string? Zip { get; init; }

    [JsonPropertyName("country_code")]
    public string? CountryCode { get; init; }

    [JsonPropertyName("phone")]
    public string? Phone { get; init; }
}

public sealed record ShopifyTransaction
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("gateway")]
    public string? Gateway { get; init; }  // "shopify_payments", "paypal", etc.

    [JsonPropertyName("status")]
    public string Status { get; init; } = default!;  // "success"

    [JsonPropertyName("amount")]
    public string Amount { get; init; } = default!;

    [JsonPropertyName("processed_at")]
    public DateTimeOffset? ProcessedAt { get; init; }
}

// ---------------------------------------------------------------------------
// Integration Messages (published to RabbitMQ for other BCs)
// ---------------------------------------------------------------------------

// These would normally live in src/Shared/Messages.Contracts/Marketplaces/
// Shown here for reference

public sealed record MarketplaceOrderReceived(
    Guid CritterOrderId,     // New GUID assigned by the Marketplaces BC
    string ChannelCode,      // "SHOPIFY_US"
    string ShopifyOrderId,   // Numeric Shopify order ID (string for cross-BC portability)
    string ShopifyTransactionId,  // For financial reconciliation (PO feedback requirement)
    string CustomerEmail,
    decimal TotalPrice,
    string Currency,
    IReadOnlyList<MarketplaceOrderLineItem> LineItems,
    MarketplaceShippingAddress ShippingAddress,
    DateTimeOffset PlacedAt);

public sealed record MarketplaceOrderLineItem(
    string Sku,
    string Title,
    int Quantity,
    decimal UnitPrice);

public sealed record MarketplaceShippingAddress(
    string FirstName,
    string LastName,
    string Address1,
    string? Address2,
    string City,
    string Province,
    string PostalCode,
    string CountryCode);

public sealed record MarketplaceOrderCancelled(
    string ChannelCode,      // "SHOPIFY_US"
    string ShopifyOrderId,
    string Reason,
    DateTimeOffset CancelledAt);

// ---------------------------------------------------------------------------
// Webhook Handler (ASP.NET Core endpoint)
// ---------------------------------------------------------------------------

/// <summary>
/// Handles all inbound Shopify webhooks at a single endpoint.
/// Shopify sends all webhook topics to the same URL (or separate URLs per topic).
/// Using a single endpoint with topic-based routing is the simpler pattern.
///
/// The endpoint must respond within 5 seconds. All heavy processing is deferred
/// to Wolverine message handlers (via RabbitMQ) for async execution.
/// </summary>
[ApiController]
[Route("webhooks/shopify")]
public sealed class ShopifyWebhookController(
    IWebhookVerificationService verificationService,
    IWebhookDeduplicationService deduplicationService,
    IMessageBus bus,
    ILogger<ShopifyWebhookController> logger) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [HttpPost]
    public async Task<IActionResult> Handle(CancellationToken ct)
    {
        // STEP 1: Read raw body bytes BEFORE any deserialization
        // ASP.NET Core may normalize JSON, breaking HMAC verification
        Request.EnableBuffering();
        var rawBody = await ReadRawBodyAsync(ct);

        // STEP 2: Extract and parse webhook headers
        var headers = ExtractHeaders();

        // STEP 3: Verify HMAC signature (security — reject spoofed webhooks)
        if (!verificationService.VerifySignature(rawBody, headers.HmacSha256))
        {
            logger.LogWarning("Shopify webhook HMAC verification failed. Topic={Topic} Shop={Shop}",
                headers.Topic, headers.ShopDomain);
            return StatusCode(401);  // Return 401; do NOT return 400 (reveals validation)
        }

        // STEP 4: Check for duplicate delivery (at-least-once delivery model)
        if (await deduplicationService.IsAlreadyProcessedAsync(headers.EventId, ct))
        {
            logger.LogInformation("Duplicate Shopify webhook ignored. EventId={EventId}", headers.EventId);
            return Ok();  // Return 200 — we processed it, just not again
        }

        // STEP 5: Route to topic-specific handler
        // Respond 200 quickly; publish to RabbitMQ for async processing
        var bodyText = Encoding.UTF8.GetString(rawBody);
        await DispatchByTopicAsync(headers, bodyText, ct);

        // STEP 6: Mark as processed (after successful publish to RabbitMQ)
        await deduplicationService.MarkProcessedAsync(headers.EventId, TimeSpan.FromHours(48), ct);

        return Ok();
    }

    private async Task DispatchByTopicAsync(ShopifyWebhookHeaders headers, string bodyText, CancellationToken ct)
    {
        switch (headers.Topic)
        {
            case "orders/create":
                await HandleOrderCreatedAsync(bodyText, headers, ct);
                break;

            case "orders/cancelled":
                await HandleOrderCancelledAsync(bodyText, headers, ct);
                break;

            case "orders/updated":
                // CAUTION (per PO feedback): orders/updated fires for ANY order change,
                // including tag additions and note changes. Only act on structural changes.
                await HandleOrderUpdatedAsync(bodyText, headers, ct);
                break;

            case "fulfillments/update":
                // Shopify-side fulfillment status changes (e.g., carrier picked up)
                // Log for monitoring; no action needed (we push status TO Shopify, not receive from it)
                logger.LogInformation("Shopify fulfillment updated (monitor only). EventId={EventId}", headers.EventId);
                break;

            case "app/uninstalled":
                // Alert + disable the Shopify adapter for this store
                logger.LogCritical("Shopify app uninstalled! Shop={Shop}. Disabling SHOPIFY_US adapter.",
                    headers.ShopDomain);
                await bus.PublishAsync(new ShopifyAppUninstalled(headers.ShopDomain), ct);
                break;

            // GDPR Mandatory Webhooks (P0 — Shopify policy requirement)
            case "customers/data_request":
                await HandleCustomerDataRequestAsync(bodyText, ct);
                break;

            case "customers/redact":
                await HandleCustomerRedactAsync(bodyText, ct);
                break;

            case "shop/redact":
                await HandleShopRedactAsync(bodyText, ct);
                break;

            default:
                logger.LogDebug("Unhandled Shopify webhook topic: {Topic}", headers.Topic);
                break;
        }
    }

    // -----------------------------------------------------------------------
    // 5.x Order Ingestion — orders/create
    // -----------------------------------------------------------------------

    private async Task HandleOrderCreatedAsync(
        string bodyText,
        ShopifyWebhookHeaders headers,
        CancellationToken ct)
    {
        var order = JsonSerializer.Deserialize<ShopifyOrderPayload>(bodyText, JsonOptions);
        if (order is null)
        {
            logger.LogError("Failed to deserialize Shopify order payload. EventId={EventId}", headers.EventId);
            return;
        }

        // Validate that payment was captured (defensive check)
        if (order.FinancialStatus is not "paid")
        {
            logger.LogWarning(
                "Shopify order {OrderId} has unexpected financialStatus={Status}. Skipping ingestion.",
                order.Id, order.FinancialStatus);
            return;
        }

        if (order.ShippingAddress is null)
        {
            logger.LogWarning("Shopify order {OrderId} has no shipping address. Cannot ingest.", order.Id);
            return;
        }

        // Map Shopify order to CritterSupply integration message
        var message = new MarketplaceOrderReceived(
            CritterOrderId: Guid.NewGuid(),  // New identity in our system
            ChannelCode: "SHOPIFY_US",
            ShopifyOrderId: order.Id.ToString(),
            ShopifyTransactionId: order.ShopifyTransactionId ?? "",  // Per PO: required for reconciliation
            CustomerEmail: order.Email ?? "",
            TotalPrice: decimal.Parse(order.TotalPrice),
            Currency: order.Currency,
            LineItems: order.LineItems.Select(li => new MarketplaceOrderLineItem(
                Sku: li.Sku ?? $"shopify-variant-{li.VariantId}",  // fallback if SKU not set
                Title: li.Title,
                Quantity: li.Quantity,
                UnitPrice: decimal.Parse(li.Price)
            )).ToList(),
            ShippingAddress: new MarketplaceShippingAddress(
                FirstName: order.ShippingAddress.FirstName ?? "",
                LastName: order.ShippingAddress.LastName ?? "",
                Address1: order.ShippingAddress.Address1 ?? "",
                Address2: order.ShippingAddress.Address2,
                City: order.ShippingAddress.City ?? "",
                Province: order.ShippingAddress.Province ?? "",
                PostalCode: order.ShippingAddress.Zip ?? "",
                CountryCode: order.ShippingAddress.CountryCode ?? "US"
            ),
            PlacedAt: order.CreatedAt);

        // Publish to RabbitMQ → Orders BC will pick up and create the order
        // The Orders saga uses PlaceMarketplaceOrder (Option A from Payment Seam ADR)
        // — bypasses Payments BC since Shopify already captured payment
        await bus.PublishAsync(message, ct);

        logger.LogInformation(
            "Shopify order ingested: ShopifyId={ShopifyId} CritterOrderId={CritterId} Items={ItemCount}",
            order.Id, message.CritterOrderId, order.LineItems.Count);
    }

    // -----------------------------------------------------------------------
    // Order Cancellation — orders/cancelled
    // -----------------------------------------------------------------------

    private async Task HandleOrderCancelledAsync(
        string bodyText,
        ShopifyWebhookHeaders headers,
        CancellationToken ct)
    {
        var order = JsonSerializer.Deserialize<ShopifyOrderPayload>(bodyText, JsonOptions);
        if (order is null) return;

        var message = new MarketplaceOrderCancelled(
            ChannelCode: "SHOPIFY_US",
            ShopifyOrderId: order.Id.ToString(),
            Reason: "customer_cancelled",  // Shopify doesn't always provide reason in webhook
            CancelledAt: DateTimeOffset.UtcNow);

        await bus.PublishAsync(message, ct);
        logger.LogInformation("Shopify order cancelled: ShopifyId={ShopifyId}", order.Id);
    }

    // -----------------------------------------------------------------------
    // Order Updated — orders/updated (conservative filtering)
    // -----------------------------------------------------------------------

    private async Task HandleOrderUpdatedAsync(
        string bodyText,
        ShopifyWebhookHeaders headers,
        CancellationToken ct)
    {
        var order = JsonSerializer.Deserialize<ShopifyOrderPayload>(bodyText, JsonOptions);
        if (order is null) return;

        // Per PO feedback: orders/updated fires for ANY change (tags, notes, etc.)
        // Only react to meaningful structural changes. For Cycle 35, log and monitor.
        // Implement full order-update reconciliation in a post-Cycle-35 follow-up.
        logger.LogInformation(
            "Shopify order updated: ShopifyId={ShopifyId} FinancialStatus={Status}",
            order.Id, order.FinancialStatus);

        // TODO (Cycle 36): Detect changes to line_items or shipping_address and
        // publish MarketplaceOrderUpdated integration message for Orders BC to handle.
    }

    // -----------------------------------------------------------------------
    // GDPR Mandatory Webhooks (P0 — Shopify policy requirement)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Mandatory GDPR: Customer requests their stored data.
    ///
    /// CritterSupply's Marketplaces BC does NOT store customer PII directly —
    /// we publish it to the Orders BC which owns the Order aggregate (including address).
    /// The response to Shopify: "orders data is owned by our Orders BC."
    ///
    /// A production implementation must respond to Shopify's data review endpoint
    /// (not just log the request). For now, log the request and notify the data privacy team.
    /// </summary>
    private async Task HandleCustomerDataRequestAsync(string bodyText, CancellationToken ct)
    {
        // Parse customer ID from payload for logging
        using var doc = JsonDocument.Parse(bodyText);
        var customerId = doc.RootElement.GetProperty("customer").GetProperty("id").GetInt64();
        var shopDomain = doc.RootElement.GetProperty("shop_domain").GetString();

        logger.LogWarning(
            "GDPR customer data request received. ShopifyCustomerId={CustomerId} Shop={Shop}. " +
            "Forward to data privacy process.",
            customerId, shopDomain);

        // Publish internal event for data privacy workflow (to be handled by Customer Identity BC)
        // In a complete implementation, this triggers a data export + report back to Shopify's API
        await Task.CompletedTask;  // Placeholder for actual GDPR workflow integration
    }

    /// <summary>
    /// Mandatory GDPR: Shopify requests deletion of customer data.
    /// Fires 48 hours after customer requests account deletion in Shopify.
    /// </summary>
    private async Task HandleCustomerRedactAsync(string bodyText, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(bodyText);
        var customerId = doc.RootElement.GetProperty("customer").GetProperty("id").GetInt64();

        logger.LogWarning(
            "GDPR customer redact request received. ShopifyCustomerId={CustomerId}. " +
            "Initiating data deletion workflow.",
            customerId);

        // Publish internal event for data deletion workflow
        await Task.CompletedTask;  // Placeholder for actual deletion workflow
    }

    /// <summary>
    /// Mandatory GDPR: Shopify requests deletion of all shop data.
    /// Fires when the merchant uninstalls the app and requests data deletion.
    /// </summary>
    private async Task HandleShopRedactAsync(string bodyText, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(bodyText);
        var shopDomain = doc.RootElement.GetProperty("shop_domain").GetString();

        logger.LogWarning(
            "GDPR shop redact request received. Shop={Shop}. Initiating full shop data deletion.",
            shopDomain);

        // All data associated with this shop's orders should be purged
        // per our data retention policy and GDPR Article 17 (right to erasure)
        await Task.CompletedTask;  // Placeholder
    }

    // -----------------------------------------------------------------------
    // Helper methods
    // -----------------------------------------------------------------------

    private async Task<byte[]> ReadRawBodyAsync(CancellationToken ct)
    {
        using var ms = new MemoryStream();
        Request.Body.Position = 0;
        await Request.Body.CopyToAsync(ms, ct);
        return ms.ToArray();
    }

    private ShopifyWebhookHeaders ExtractHeaders() => new()
    {
        Topic = Request.Headers["X-Shopify-Topic"].FirstOrDefault() ?? "",
        HmacSha256 = Request.Headers["X-Shopify-Hmac-Sha256"].FirstOrDefault() ?? "",
        ShopDomain = Request.Headers["X-Shopify-Shop-Domain"].FirstOrDefault() ?? "",
        WebhookId = Request.Headers["X-Shopify-Webhook-Id"].FirstOrDefault() ?? "",
        EventId = Request.Headers["X-Shopify-Event-Id"].FirstOrDefault() ?? "",
        TriggeredAt = DateTimeOffset.TryParse(
            Request.Headers["X-Shopify-Triggered-At"].FirstOrDefault(),
            out var ts) ? ts : DateTimeOffset.UtcNow
    };
}

// ---------------------------------------------------------------------------
// Webhook Signature Verification Service
// ---------------------------------------------------------------------------

public interface IWebhookVerificationService
{
    bool VerifySignature(ReadOnlySpan<byte> rawBody, string hmacHeader);
}

/// <summary>
/// Verifies Shopify webhook authenticity using HMAC-SHA256.
///
/// Algorithm:
/// 1. Compute HMAC-SHA256 of raw request body bytes using webhook_secret as key
/// 2. Base64-encode the computed hash
/// 3. Compare to X-Shopify-Hmac-Sha256 header using constant-time comparison
///
/// CRITICAL: Use constant-time comparison (CryptographicOperations.FixedTimeEquals)
/// to prevent timing attacks — an attacker could otherwise determine the correct
/// HMAC character by character by measuring response time differences.
/// </summary>
public sealed class ShopifyWebhookVerificationService(
    ShopifyCredentials credentials) : IWebhookVerificationService
{
    public bool VerifySignature(ReadOnlySpan<byte> rawBody, string hmacHeader)
    {
        if (string.IsNullOrEmpty(hmacHeader)) return false;

        var secretBytes = Encoding.UTF8.GetBytes(credentials.WebhookSecret);
        var computedHash = HMACSHA256.HashData(secretBytes, rawBody);
        var computedBase64 = Convert.ToBase64String(computedHash);

        // Constant-time comparison prevents timing attacks
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computedBase64),
            Encoding.UTF8.GetBytes(hmacHeader));
    }
}

// ---------------------------------------------------------------------------
// Webhook Deduplication Service
// ---------------------------------------------------------------------------

public interface IWebhookDeduplicationService
{
    Task<bool> IsAlreadyProcessedAsync(string eventId, CancellationToken ct);
    Task MarkProcessedAsync(string eventId, TimeSpan ttl, CancellationToken ct);
}

/// <summary>
/// Deduplicates Shopify webhooks using X-Shopify-Event-Id as the key.
///
/// Shopify guarantees at-least-once delivery — the same event may be delivered
/// multiple times. Use X-Shopify-Event-Id (stable across retry attempts) as the
/// deduplication key, not X-Shopify-Webhook-Id (unique per delivery attempt).
///
/// TTL should match Shopify's retry window (48 hours) to catch all retries.
///
/// Production implementation options:
/// - Redis (preferred for performance — O(1) lookup, TTL built-in)
/// - Marten document store (acceptable for low-volume; adds DB round-trip)
/// - Postgres table with auto-delete on expiry (most durable option)
///
/// This example uses a simple in-memory implementation for reference clarity.
/// Replace with Redis or Marten in production.
/// </summary>
public sealed class InMemoryWebhookDeduplicationService : IWebhookDeduplicationService
{
    // In production: replace with Redis SETNX with TTL, or Marten document
    private readonly Dictionary<string, DateTimeOffset> _processed = new();
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<bool> IsAlreadyProcessedAsync(string eventId, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_processed.TryGetValue(eventId, out var expiry))
                return DateTimeOffset.UtcNow < expiry;
            return false;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task MarkProcessedAsync(string eventId, TimeSpan ttl, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            _processed[eventId] = DateTimeOffset.UtcNow + ttl;
        }
        finally
        {
            _lock.Release();
        }
    }
}

// ---------------------------------------------------------------------------
// Integration event for app uninstall detection
// ---------------------------------------------------------------------------

public sealed record ShopifyAppUninstalled(string ShopDomain);

// ---------------------------------------------------------------------------
// IMessageBus interface (placeholder — use Wolverine's actual IMessageBus)
// ---------------------------------------------------------------------------

// In real implementation, inject Wolverine's IMessageBus from JasperFx.Core
public interface IMessageBus
{
    Task PublishAsync<T>(T message, CancellationToken ct) where T : notnull;
}
