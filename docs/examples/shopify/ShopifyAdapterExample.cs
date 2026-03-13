// =============================================================================
// ShopifyAdapterExample.cs
// Reference implementation of IMarketplaceAdapter for Shopify
//
// PURPOSE: Demonstrates production-ready patterns for:
//   - Catalog sync (productSet GraphQL mutation)
//   - Product deactivation (recall cascade support)
//   - Inventory updates (absolute on-hand quantities)
//   - Fulfillment push-back (tracking info after shipment)
//   - Rate limit handling (exponential backoff)
//   - GraphQL error handling (HTTP 200 with userErrors)
//
// This file is a REFERENCE EXAMPLE only. It shows the patterns
// and structures to use when implementing the Shopify adapter in
// src/Marketplaces/Marketplaces/Adapters/ShopifyAdapter.cs (Cycle 35).
//
// API Version: 2026-01 (GraphQL Admin API)
// Authentication: Custom App (admin-created), token in Vault
// =============================================================================

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Marketplaces.Adapters.Shopify;

// ---------------------------------------------------------------------------
// Configuration / Credentials (loaded from Vault)
// ---------------------------------------------------------------------------

/// <summary>
/// Shopify credentials loaded from Vault at the path stored on the Marketplace document.
/// Shape matches: vault kv get secret/marketplaces/SHOPIFY_US
/// </summary>
public sealed record ShopifyCredentials
{
    /// <summary>The .myshopify.com domain — permanent, never changes.</summary>
    public string StoreDomain { get; init; } = default!;

    /// <summary>Admin API access token. Prefix: shpat_ for admin-created Custom Apps.</summary>
    public string AccessToken { get; init; } = default!;

    /// <summary>Shared secret for HMAC-SHA256 webhook verification.</summary>
    public string WebhookSecret { get; init; } = default!;

    /// <summary>
    /// Pinned API version string. Stored in Vault so version updates don't require deployment.
    /// Example: "2026-01"
    /// </summary>
    public string ApiVersion { get; init; } = "2026-01";

    /// <summary>Derived GraphQL endpoint.</summary>
    public string GraphQlEndpoint =>
        $"https://{StoreDomain}/admin/api/{ApiVersion}/graphql.json";
}

// ---------------------------------------------------------------------------
// Core Data Models
// ---------------------------------------------------------------------------

/// <summary>Maps to CritterSupply's ProductFamily. Shopify Product = our ProductFamily.</summary>
public sealed record ShopifyProduct
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = default!;  // gid://shopify/Product/{numeric_id}

    [JsonPropertyName("title")]
    public string Title { get; init; } = default!;

    [JsonPropertyName("status")]
    public string Status { get; init; } = default!;  // ACTIVE | DRAFT | ARCHIVED

    [JsonPropertyName("handle")]
    public string Handle { get; init; } = default!;  // URL-safe slug

    [JsonPropertyName("variants")]
    public ShopifyVariantConnection? Variants { get; init; }
}

/// <summary>Maps to CritterSupply's ProductVariant. Shopify ProductVariant = our ProductVariant.</summary>
public sealed record ShopifyProductVariant
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = default!;  // gid://shopify/ProductVariant/{id}

    [JsonPropertyName("sku")]
    public string? Sku { get; init; }

    [JsonPropertyName("inventoryItem")]
    public ShopifyInventoryItem? InventoryItem { get; init; }
}

public sealed record ShopifyInventoryItem
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = default!;  // gid://shopify/InventoryItem/{id}
}

public sealed record ShopifyVariantConnection
{
    [JsonPropertyName("edges")]
    public IReadOnlyList<ShopifyVariantEdge> Edges { get; init; } = [];
}

public sealed record ShopifyVariantEdge
{
    [JsonPropertyName("node")]
    public ShopifyProductVariant Node { get; init; } = default!;
}

/// <summary>
/// GraphQL response envelope. Always check both Errors (schema-level)
/// and userErrors (mutation business-level) before treating as success.
/// GraphQL returns HTTP 200 even for business errors — do NOT rely on status code alone.
/// </summary>
public sealed record GraphQlResponse<T>
{
    [JsonPropertyName("data")]
    public T? Data { get; init; }

    [JsonPropertyName("errors")]
    public IReadOnlyList<GraphQlError>? Errors { get; init; }

    [JsonPropertyName("extensions")]
    public GraphQlExtensions? Extensions { get; init; }

    public bool HasSchemaErrors => Errors is { Count: > 0 };
    public bool IsThrottled => Errors?.Any(e =>
        e.Extensions?.TryGetValue("code", out var code) == true &&
        code?.ToString() == "THROTTLED") == true;
}

public sealed record GraphQlError
{
    [JsonPropertyName("message")]
    public string Message { get; init; } = default!;

    [JsonPropertyName("extensions")]
    public Dictionary<string, object>? Extensions { get; init; }
}

public sealed record GraphQlExtensions
{
    [JsonPropertyName("cost")]
    public GraphQlCost? Cost { get; init; }
}

public sealed record GraphQlCost
{
    [JsonPropertyName("requestedQueryCost")]
    public int RequestedQueryCost { get; init; }

    [JsonPropertyName("actualQueryCost")]
    public int ActualQueryCost { get; init; }

    [JsonPropertyName("throttleStatus")]
    public GraphQlThrottleStatus? ThrottleStatus { get; init; }
}

public sealed record GraphQlThrottleStatus
{
    [JsonPropertyName("currentlyAvailable")]
    public double CurrentlyAvailable { get; init; }

    [JsonPropertyName("restoreRate")]
    public double RestoreRate { get; init; }
}

/// <summary>Business-level error returned in mutation payloads (not HTTP-level errors).</summary>
public sealed record ShopifyUserError
{
    [JsonPropertyName("field")]
    public string Field { get; init; } = default!;

    [JsonPropertyName("message")]
    public string Message { get; init; } = default!;

    [JsonPropertyName("code")]
    public string? Code { get; init; }
}

// ---------------------------------------------------------------------------
// Adapter Interface (mirrors the existing IMarketplaceAdapter pattern)
// ---------------------------------------------------------------------------

public interface IMarketplaceAdapter
{
    Task<SubmissionResult> SubmitListingAsync(ListingSubmission listing, CancellationToken ct);
    Task<DeactivationResult> DeactivateListingAsync(string channelProductId, CancellationToken ct);
    Task<InventorySyncResult> UpdateInventoryAsync(string sku, string channelInventoryItemId, int availableQuantity, string locationId, CancellationToken ct);
    Task<FulfillmentResult> ReportFulfillmentAsync(ShopifyFulfillmentRequest request, CancellationToken ct);
}

public sealed record ListingSubmission(
    string FamilyId,
    string FamilyName,
    string? DescriptionHtml,
    string? Vendor,
    string ProductType,    // Maps to Shopify product_type
    IReadOnlyList<string> Tags,
    IReadOnlyList<ListingVariant> Variants);

public sealed record ListingVariant(
    string Sku,
    decimal Price,
    decimal? CompareAtPrice,
    double WeightPounds,
    string? Barcode,
    bool IsHazmat,
    string? HazmatClass,
    IReadOnlyList<VariantOptionValue>? OptionValues = null);

/// <summary>Maps a variant to a specific option selection (e.g., Size=5lb, Flavor=Salmon).</summary>
public sealed record VariantOptionValue(string OptionName, string Value);

public sealed record SubmissionResult(bool Success, string? ChannelProductId, IReadOnlyList<string> Errors);
public sealed record DeactivationResult(bool Success, IReadOnlyList<string> Errors);
public sealed record InventorySyncResult(bool Success, IReadOnlyList<string> Errors);
public sealed record ShopifyFulfillmentRequest(string ShopifyOrderId, string FulfillmentOrderId, string TrackingNumber, string Carrier, string? TrackingUrl);
public sealed record FulfillmentResult(bool Success, string? FulfillmentId, IReadOnlyList<string> Errors);

// ---------------------------------------------------------------------------
// Shopify Adapter Implementation
// ---------------------------------------------------------------------------

/// <summary>
/// Production-ready Shopify adapter implementing IMarketplaceAdapter.
///
/// Key design decisions:
/// - Uses GraphQL Admin API exclusively (REST Product API deprecated 2024-04)
/// - productSet mutation for idempotent catalog sync (external-system-is-source-of-truth model)
/// - ARCHIVED status for paused/ended/recalled listings (not DELETE — preserves order history)
/// - Absolute on-hand inventory quantities (not delta adjustments — safer for distributed systems)
/// - Exponential backoff for THROTTLED errors (Shopify leaky bucket: 100 points/sec standard)
/// </summary>
public sealed class ShopifyAdapter(
    IHttpClientFactory httpClientFactory,
    ShopifyCredentials credentials,
    ILogger<ShopifyAdapter> logger) : IMarketplaceAdapter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // -----------------------------------------------------------------------
    // 4.1 Catalog Sync: Create or Update Product (productSet mutation)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Syncs a product listing to Shopify using the productSet mutation.
    ///
    /// productSet is IDEMPOTENT — safe to call multiple times for the same product.
    /// If the product doesn't exist, it's created. If it exists, it's replaced entirely.
    /// No explicit idempotency key required (unlike Stripe).
    ///
    /// Shopify Product = CritterSupply ProductFamily
    /// Shopify ProductVariant = CritterSupply ProductVariant
    ///
    /// This is the correct mutation when "an external system is the source of truth"
    /// (per Shopify's product model migration guide). Our Product Catalog BC is the
    /// source of truth — Shopify receives a projection of it.
    /// </summary>
    public async Task<SubmissionResult> SubmitListingAsync(
        ListingSubmission listing,
        CancellationToken ct)
    {
        const string mutation = """
            mutation ProductSync($input: ProductSetInput!) {
              productSet(input: $input) {
                product {
                  id
                  handle
                  status
                  variants(first: 100) {
                    edges {
                      node {
                        id
                        sku
                        inventoryItem { id }
                      }
                    }
                  }
                }
                userErrors {
                  field
                  message
                  code
                }
              }
            }
            """;

        var variables = new
        {
            input = new
            {
                title = listing.FamilyName,
                descriptionHtml = listing.DescriptionHtml,
                vendor = listing.Vendor,
                productType = listing.ProductType,
                status = "ACTIVE",
                tags = listing.Tags,
                // Options are derived from variant structure. For pet food example:
                // ProductVariants with different sizes → Option: "Size" with values
                // options must be declared separately; optionValues on each variant references them
                variants = listing.Variants.Select(v => new
                {
                    sku = v.Sku,
                    price = v.Price.ToString("F2"),
                    compareAtPrice = v.CompareAtPrice?.ToString("F2"),
                    barcode = v.Barcode,
                    weight = v.WeightPounds,
                    weightUnit = "POUNDS",
                    requiresShipping = true,
                    taxable = true,
                    // DENY: never allow overselling. Pet food orders are often subscription-critical.
                    inventoryPolicy = "DENY",
                    inventoryManagement = "SHOPIFY",
                    // optionValues maps this variant to specific option selections (e.g., Size=5lb).
                    // Populated from the variant's option attributes in the real implementation.
                    // Without this, Shopify cannot associate variants with options.
                    // Example: optionValues = [ { optionName: "Size", name: v.SizeName } ]
                    optionValues = v.OptionValues?.Select(o => new { optionName = o.OptionName, name = o.Value })
                                   ?? Array.Empty<object>(),
                    // Compliance metafields (elevated to P1 by PO review)
                    metafields = BuildComplianceMetafields(v)
                })
            }
        };

        var response = await ExecuteGraphQlWithRetryAsync<ProductSetResponse>(
            mutation, variables, ct);

        if (response.Data?.ProductSet?.UserErrors is { Count: > 0 } errors)
        {
            var errorMessages = errors.Select(e => $"[{e.Field}] {e.Message}").ToList();
            logger.LogWarning("Shopify productSet rejected for {FamilyId}: {Errors}",
                listing.FamilyId, string.Join("; ", errorMessages));
            return new SubmissionResult(false, null, errorMessages);
        }

        var product = response.Data?.ProductSet?.Product;
        if (product is null)
        {
            return new SubmissionResult(false, null, ["Shopify returned no product in response"]);
        }

        logger.LogInformation("Shopify listing synced: {ProductId} for family {FamilyId}",
            product.Id, listing.FamilyId);

        return new SubmissionResult(true, product.Id, []);
    }

    private static object[] BuildComplianceMetafields(ListingVariant variant)
    {
        // Namespace: critter_supply — avoids collision with Shopify system metafields
        var fields = new List<object>
        {
            new { @namespace = "critter_supply", key = "is_hazmat",
                  value = variant.IsHazmat ? "true" : "false",
                  type = "single_line_text_field" }
        };

        if (variant.IsHazmat && variant.HazmatClass is not null)
        {
            fields.Add(new { @namespace = "critter_supply", key = "hazmat_class",
                value = variant.HazmatClass, type = "single_line_text_field" });
        }

        return [.. fields];
    }

    // -----------------------------------------------------------------------
    // 4.2 Deactivate Listing (recall cascade, season end, etc.)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Deactivates a Shopify listing by setting its status to ARCHIVED.
    ///
    /// Why ARCHIVED and not DELETE?
    /// - ARCHIVED hides the product from customers but preserves it in Shopify's history
    /// - Historical orders still reference the product correctly (customer-facing order history)
    /// - Deactivated products can be restored (re-ACTIVE) if needed
    /// - DELETE is permanent and breaks historical order references — never use for recall cascades
    ///
    /// Mapping:
    ///   ListingPaused     → ARCHIVED
    ///   ListingEnded      → ARCHIVED
    ///   ProductRecalled   → ARCHIVED (with highest-priority routing via product-recall exchange)
    /// </summary>
    public async Task<DeactivationResult> DeactivateListingAsync(
        string channelProductId,  // gid://shopify/Product/{id}
        CancellationToken ct)
    {
        const string mutation = """
            mutation DeactivateListing($id: ID!) {
              productUpdate(input: { id: $id, status: ARCHIVED }) {
                product {
                  id
                  status
                }
                userErrors {
                  field
                  message
                  code
                }
              }
            }
            """;

        var response = await ExecuteGraphQlWithRetryAsync<ProductUpdateResponse>(
            mutation, new { id = channelProductId }, ct);

        if (response.Data?.ProductUpdate?.UserErrors is { Count: > 0 } errors)
        {
            var errorMessages = errors.Select(e => $"[{e.Field}] {e.Message}").ToList();
            logger.LogWarning("Shopify product deactivation failed for {ProductId}: {Errors}",
                channelProductId, string.Join("; ", errorMessages));
            return new DeactivationResult(false, errorMessages);
        }

        logger.LogInformation("Shopify listing deactivated: {ProductId}", channelProductId);
        return new DeactivationResult(true, []);
    }

    // -----------------------------------------------------------------------
    // 4.3 Inventory Update (absolute on-hand quantity)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Updates available inventory for a specific SKU at a specific Shopify location.
    ///
    /// Uses ABSOLUTE on-hand quantity (not delta adjustments). This is safer in a
    /// distributed system: if a message is lost or processed out of order, pushing
    /// the current absolute quantity from Inventory BC recovers to correct state.
    /// Delta adjustments would permanently drift if any message is skipped.
    ///
    /// The inventoryItemId and locationId are obtained once when the product is first
    /// synced (from the productSet response) and stored on the Listing entity.
    /// </summary>
    public async Task<InventorySyncResult> UpdateInventoryAsync(
        string sku,
        string channelInventoryItemId,  // gid://shopify/InventoryItem/{id}
        int availableQuantity,
        string locationId,              // gid://shopify/Location/{id}
        CancellationToken ct)
    {
        const string mutation = """
            mutation UpdateInventory($input: InventorySetOnHandQuantitiesInput!) {
              inventorySetOnHandQuantities(input: $input) {
                inventoryAdjustmentGroup {
                  id
                  changes {
                    name
                    delta
                    quantityAfterChange
                  }
                }
                userErrors {
                  field
                  message
                  code
                }
              }
            }
            """;

        var variables = new
        {
            input = new
            {
                reason = "correction",
                setQuantities = new[]
                {
                    new
                    {
                        inventoryItemId = channelInventoryItemId,
                        locationId,
                        quantity = availableQuantity
                    }
                }
            }
        };

        var response = await ExecuteGraphQlWithRetryAsync<InventorySetResponse>(
            mutation, variables, ct);

        if (response.Data?.InventorySetOnHandQuantities?.UserErrors is { Count: > 0 } errors)
        {
            var errorMessages = errors.Select(e => $"[{e.Field}] {e.Message}").ToList();
            logger.LogWarning("Shopify inventory update failed for SKU {Sku}: {Errors}",
                sku, string.Join("; ", errorMessages));
            return new InventorySyncResult(false, errorMessages);
        }

        logger.LogInformation("Shopify inventory updated: SKU={Sku} Qty={Qty} Location={Location}",
            sku, availableQuantity, locationId);
        return new InventorySyncResult(true, []);
    }

    // -----------------------------------------------------------------------
    // 4.5 Fulfillment Push-Back
    // -----------------------------------------------------------------------

    /// <summary>
    /// Reports tracking information to Shopify after CritterSupply's Fulfillment BC
    /// has shipped the order. Shopify uses this to:
    /// 1. Mark the order as "Fulfilled" in the Shopify admin
    /// 2. Send the customer a "your order has shipped" email with tracking link
    ///
    /// IMPORTANT: The handler for ShipmentDispatched events must call this SYNCHRONOUSLY
    /// (within the Wolverine handler, not deferred to a background job). Shopify's order
    /// status page shows "Unfulfilled" until this call succeeds. Every minute of delay
    /// is visible to the customer.
    ///
    /// Prerequisite: Retrieve the FulfillmentOrder ID from the order first.
    /// The Shopify order model is: Order → FulfillmentOrder(s) → Fulfillment(s)
    /// The FulfillmentOrderId is stored on the CritterSupply Order aggregate when the
    /// order is ingested (from the orders/create webhook payload).
    /// </summary>
    public async Task<FulfillmentResult> ReportFulfillmentAsync(
        ShopifyFulfillmentRequest request,
        CancellationToken ct)
    {
        const string mutation = """
            mutation CreateFulfillment($fulfillment: FulfillmentInput!) {
              fulfillmentCreate(fulfillment: $fulfillment) {
                fulfillment {
                  id
                  status
                  trackingInfo {
                    company
                    number
                    url
                  }
                }
                userErrors {
                  field
                  message
                  code
                }
              }
            }
            """;

        var variables = new
        {
            fulfillment = new
            {
                lineItemsByFulfillmentOrder = new[]
                {
                    new { fulfillmentOrderId = request.FulfillmentOrderId }
                },
                trackingInfo = new
                {
                    company = request.Carrier,
                    number = request.TrackingNumber,
                    url = request.TrackingUrl ?? BuildTrackingUrl(request.Carrier, request.TrackingNumber)
                },
                notifyCustomer = true  // Shopify sends "your order has shipped" email
            }
        };

        var response = await ExecuteGraphQlWithRetryAsync<FulfillmentCreateResponse>(
            mutation, variables, ct);

        if (response.Data?.FulfillmentCreate?.UserErrors is { Count: > 0 } errors)
        {
            var errorMessages = errors.Select(e => $"[{e.Field}] {e.Message}").ToList();
            logger.LogWarning("Shopify fulfillment create failed for Order {OrderId}: {Errors}",
                request.ShopifyOrderId, string.Join("; ", errorMessages));
            return new FulfillmentResult(false, null, errorMessages);
        }

        var fulfillmentId = response.Data?.FulfillmentCreate?.Fulfillment?.Id;
        logger.LogInformation("Shopify fulfillment created: {FulfillmentId} for Order {OrderId}",
            fulfillmentId, request.ShopifyOrderId);

        return new FulfillmentResult(true, fulfillmentId, []);
    }

    // -----------------------------------------------------------------------
    // Rate Limit Handling — Exponential Backoff
    // -----------------------------------------------------------------------

    /// <summary>
    /// Executes a GraphQL mutation with exponential backoff on THROTTLED errors.
    ///
    /// Shopify GraphQL rate limit: 100 points/second (standard), 1,000 points/second (Plus).
    /// When the bucket is empty, Shopify returns HTTP 200 with errors[0].extensions.code = "THROTTLED".
    ///
    /// Note: Unlike REST (which returns HTTP 429), GraphQL throttle errors use HTTP 200.
    /// Always inspect the response body, not just the HTTP status code.
    /// </summary>
    private async Task<GraphQlResponse<T>> ExecuteGraphQlWithRetryAsync<T>(
        string query,
        object variables,
        CancellationToken ct,
        int maxRetries = 5)
    {
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            var response = await ExecuteGraphQlAsync<T>(query, variables, ct);

            if (!response.IsThrottled)
                return response;

            // Calculate wait time from throttle status if available
            var restoreRate = response.Extensions?.Cost?.ThrottleStatus?.RestoreRate ?? 50.0;
            var delay = TimeSpan.FromSeconds(Math.Max(1, Math.Pow(2, attempt)));

            logger.LogWarning("Shopify rate limit hit (attempt {Attempt}/{Max}). Waiting {Delay}s. RestoreRate={Rate}",
                attempt + 1, maxRetries, delay.TotalSeconds, restoreRate);

            await Task.Delay(delay, ct);
        }

        throw new ShopifyRateLimitException(
            $"Shopify rate limit exceeded after {maxRetries} retries");
    }

    private async Task<GraphQlResponse<T>> ExecuteGraphQlAsync<T>(
        string query,
        object variables,
        CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient("Shopify");
        client.DefaultRequestHeaders.Add("X-Shopify-Access-Token", credentials.AccessToken);

        var payload = JsonSerializer.Serialize(
            new { query, variables },
            JsonOptions);

        var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var httpResponse = await client.PostAsync(credentials.GraphQlEndpoint, content, ct);

        httpResponse.EnsureSuccessStatusCode();  // Only fails on network/auth errors; not on business errors

        var responseBody = await httpResponse.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<GraphQlResponse<T>>(responseBody, JsonOptions)
               ?? throw new InvalidOperationException("Shopify returned null response");
    }

    private static string? BuildTrackingUrl(string carrier, string trackingNumber)
    {
        var encoded = Uri.EscapeDataString(trackingNumber);
        return carrier.ToUpperInvariant() switch
        {
            "UPS" => $"https://www.ups.com/track?tracknum={encoded}",
            "FEDEX" => $"https://www.fedex.com/fedextrack/?trknbr={encoded}",
            "USPS" => $"https://tools.usps.com/go/TrackConfirmAction?qtc_tLabels1={encoded}",
            _ => null
        };
    }
}

// ---------------------------------------------------------------------------
// Response type wrappers (used for JSON deserialization of GraphQL responses)
// ---------------------------------------------------------------------------

file sealed record ProductSetResponse
{
    [JsonPropertyName("productSet")]
    public ProductSetPayload? ProductSet { get; init; }
}

file sealed record ProductSetPayload
{
    [JsonPropertyName("product")]
    public ShopifyProduct? Product { get; init; }

    [JsonPropertyName("userErrors")]
    public IReadOnlyList<ShopifyUserError>? UserErrors { get; init; }
}

file sealed record ProductUpdateResponse
{
    [JsonPropertyName("productUpdate")]
    public ProductUpdatePayload? ProductUpdate { get; init; }
}

file sealed record ProductUpdatePayload
{
    [JsonPropertyName("product")]
    public ShopifyProduct? Product { get; init; }

    [JsonPropertyName("userErrors")]
    public IReadOnlyList<ShopifyUserError>? UserErrors { get; init; }
}

file sealed record InventorySetResponse
{
    [JsonPropertyName("inventorySetOnHandQuantities")]
    public InventorySetPayload? InventorySetOnHandQuantities { get; init; }
}

file sealed record InventorySetPayload
{
    [JsonPropertyName("inventoryAdjustmentGroup")]
    public object? InventoryAdjustmentGroup { get; init; }

    [JsonPropertyName("userErrors")]
    public IReadOnlyList<ShopifyUserError>? UserErrors { get; init; }
}

file sealed record FulfillmentCreateResponse
{
    [JsonPropertyName("fulfillmentCreate")]
    public FulfillmentCreatePayload? FulfillmentCreate { get; init; }
}

file sealed record FulfillmentCreatePayload
{
    [JsonPropertyName("fulfillment")]
    public ShopifyFulfillment? Fulfillment { get; init; }

    [JsonPropertyName("userErrors")]
    public IReadOnlyList<ShopifyUserError>? UserErrors { get; init; }
}

file sealed record ShopifyFulfillment
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = default!;

    [JsonPropertyName("status")]
    public string Status { get; init; } = default!;
}

// ---------------------------------------------------------------------------
// Custom exceptions
// ---------------------------------------------------------------------------

public sealed class ShopifyRateLimitException(string message) : Exception(message);
