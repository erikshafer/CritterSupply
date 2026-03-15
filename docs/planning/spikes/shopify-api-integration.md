# Shopify API Integration Research Spike

**Date:** 2026-03-13
**Author:** UX Engineer (research lead) — Engineering team owns Cycle 35 implementation
**Status:** ✅ Approved as Research Foundation — PO Review Complete (2026-03-13)
**Purpose:** Understand Shopify's API patterns, authentication model, webhooks, and e-commerce paradigms to guide future implementation of the Marketplaces BC Shopify adapter (Cycle 35)

---

## Executive Summary

This document provides a comprehensive overview of Shopify's API structure and integration patterns to guide implementation of a production-ready Shopify channel adapter within CritterSupply's Marketplaces bounded context. The research was preceded by a Product Owner discovery session — PO feedback is captured at the end of this document.

**Relationship Model:** CritterSupply operates a Shopify storefront as an **additional D2C sales channel** — `SHOPIFY_US` is a `ChannelCode` in our Marketplaces BC, alongside `AMAZON_US`, `WALMART_US`, and `OWN_WEBSITE`. The Shopify storefront is a separate consumer-facing URL (e.g., `critter-supply.myshopify.com`) marketed under the same CritterSupply brand. It is not a replacement for our own Blazor Storefront (`Storefront.Web`), which remains the primary brand experience and reference architecture showcase. Shopify provides access to customers who discover products via Shopify's ecosystem, the Shop app, and social shopping integrations (Instagram Shop, TikTok Shopping).

**Key Findings:**
- Shopify's **GraphQL Admin API** is the recommended path for new integrations; the REST Admin API's Product endpoints were deprecated as of API version `2024-04`
- The **`productSet` mutation** (GraphQL) is the recommended sync mechanism for external catalog sources — it creates or replaces full product state atomically, making it the natural fit for our Marketplaces BC adapter
- Shopify uses a **three-tier product hierarchy**: `Product` → `Options` → `Variants`, which maps directly to CritterSupply's `ProductFamily` → `Options` → `ProductVariant` model with **zero transformation required**
- Shopify **captures payment at checkout** — before CritterSupply sees the order. This creates an **architectural seam** with our Orders saga that requires a dedicated ADR before Cycle 35 implementation begins
- Webhooks are **at-least-once** with HMAC-SHA256 verification; the `X-Shopify-Event-Id` header enables deduplication
- **GraphQL Bulk Operations** are the correct answer for initial catalog sync of thousands of SKUs; regular GraphQL mutations are rate-limited by calculated query cost (100 points/second standard, 1000 points/second on Shopify Plus)
- **Custom App tokens do not expire** when created in the Shopify admin; this simplifies Vault rotation requirements but requires treating the token as a long-lived credential that must be revocable

---

## Table of Contents

1. [Core Shopify Concepts](#1-core-shopify-concepts)
2. [Authentication](#2-authentication)
3. [GraphQL Admin API — The Preferred Path](#3-graphql-admin-api--the-preferred-path)
4. [API Operations](#4-api-operations)
5. [Webhooks](#5-webhooks)
6. [The Payment Seam](#6-the-payment-seam)
7. [Rate Limits & Bulk Operations](#7-rate-limits--bulk-operations)
8. [Pet Supply Specific Gotchas](#8-pet-supply-specific-gotchas)
9. [Category Mapping Analysis](#9-category-mapping-analysis)
10. [API Version Pinning Policy](#10-api-version-pinning-policy)
11. [Scope for Cycle 35 Shopify Adapter](#11-scope-for-cycle-35-shopify-adapter)
12. [Product Owner Review & Feedback](#12-product-owner-review--feedback)

---

## 1. Core Shopify Concepts

### 1.1 Product (= CritterSupply `ProductFamily`)

Shopify's `Product` is the parent container — equivalent to our `ProductFamily`. It holds merchandising information (title, description, vendor, images) and contains options (Size, Flavor, etc.) and variants.

**Key properties:**
```json
{
  "id": "gid://shopify/Product/632910392",
  "title": "Premium Salmon Dog Food",
  "descriptionHtml": "<p>Rich in Omega-3...</p>",
  "vendor": "NutriPaws",
  "productType": "Dog Food",
  "status": "ACTIVE",
  "tags": ["dog", "food", "salmon", "grain-free"],
  "options": [
    { "id": "gid://shopify/ProductOption/1", "name": "Size", "position": 1,
      "optionValues": [{ "name": "5lb" }, { "name": "15lb" }, { "name": "30lb" }] }
  ],
  "variants": { "edges": [{ "node": { ... } }] },
  "createdAt": "2026-01-15T10:30:00Z",
  "updatedAt": "2026-02-01T14:22:00Z"
}
```

**Product statuses:**
- `ACTIVE` — live, purchasable by customers
- `DRAFT` — not yet published, only visible in Shopify admin
- `ARCHIVED` — no longer for sale, hidden from customers

**Mapping to our domain:**
- `ProductFamily` → Shopify `Product`
- `ProductFamily.Name` → Shopify `Product.title`
- `ProductFamily.Description` → Shopify `Product.descriptionHtml`
- `ProductFamily.Options` → Shopify `Product.options` (max 3)

### 1.2 ProductVariant (= CritterSupply `ProductVariant`)

Shopify's `ProductVariant` represents a specific purchasable SKU — equivalent to our `ProductVariant`. This is where price, inventory tracking, weight, and barcode live.

**Key properties:**
```json
{
  "id": "gid://shopify/ProductVariant/808950810",
  "title": "5lb",
  "sku": "DOG-SAL-5LB",
  "price": "29.99",
  "compareAtPrice": "34.99",
  "barcode": "123456789012",
  "weight": 5.0,
  "weightUnit": "POUNDS",
  "inventoryItem": {
    "id": "gid://shopify/InventoryItem/49148385198",
    "tracked": true
  },
  "selectedOptions": [
    { "name": "Size", "value": "5lb" }
  ],
  "fulfillmentService": "manual",
  "requiresShipping": true,
  "taxable": true
}
```

**Mapping to our domain:**
- `ProductVariant` → Shopify `ProductVariant`
- `ProductVariant.Sku` → Shopify `ProductVariant.sku` (the key correlation identifier)
- `ProductVariant.Price` → Shopify `ProductVariant.price`
- `ProductVariant.Weight` → Shopify `ProductVariant.weight` + `weightUnit`
- `ProductVariant.Barcode` → Shopify `ProductVariant.barcode`

> **Zero-transformation alignment:** CritterSupply's D1 decision (Option A: Parent/Child) maps directly to Shopify's `Product`/`ProductVariant` model. No translation layer required. This was by design — the variant model document explicitly notes that Option A "maps directly to Shopify's model with zero transformation."

### 1.3 InventoryItem and InventoryLevel

Shopify tracks inventory through two separate resources:

- **`InventoryItem`**: The physical product (has a 1:1 relationship with each `ProductVariant`). Contains the SKU and whether inventory is tracked. ID format: `gid://shopify/InventoryItem/{id}`
- **`InventoryLevel`**: The quantity available at a specific **Location**. One `InventoryItem` has one `InventoryLevel` per Location where it is stocked.
- **`Location`**: A geographic fulfillment location (warehouse, retail store). CritterSupply will typically have one location initially.

**Inventory model overview:**
```
ProductVariant ──1:1── InventoryItem ──1:N── InventoryLevel
                                               │
                                               ├─ Location A: qty=45
                                               └─ Location B: qty=12
```

**Note on location selection:** Shopify decrements the location with the lowest numeric ID at order placement. CritterSupply starts with a single warehouse location — in the single-location case, there is no ambiguity about which location Shopify decrements. When multi-location support is added (Section 11.2 deferred), the adapter must specify the correct `locationId` in all inventory sync calls.

**Critical behavior — Dual-Decrement Problem:** When an order is placed on Shopify, Shopify **automatically decrements** its own inventory counter at the location with the lowest ID. When CritterSupply then ingests the order and our Inventory BC creates a reservation, that is a **second decrement** of the same stock. This creates a race condition:

```
T=0: CritterSupply has 10 units. Shopify shows 10.
T=1: Customer buys 2 on Shopify → Shopify decrements: Shopify shows 8.
T=2: orders/create webhook fires → CritterSupply ingests order.
T=3: Inventory BC reservation committed → CritterSupply available: 8.
T=4: Inventory sync pushes absolute quantity to Shopify → Shopify shows 8. ✅

PROBLEM: If T=4 happens BEFORE T=3 is committed:
T=4 (early): Inventory sync reads 10 (reservation not yet committed) → pushes 10 to Shopify.
Result: Shopify shows 10 again — Shopify's own decrement is effectively undone. 🔴
```

**Mitigation:** The `MarketplaceOrderReceived` handler must commit the Inventory BC reservation BEFORE triggering the Shopify inventory sync. The Wolverine handler ordering must enforce this sequence. The inventory sync push to Shopify should be a separate, subsequent step that reads the post-reservation quantity. This is not a theoretical concern — it will occur in testing when orders arrive quickly. Engineering must design the Wolverine handler chain explicitly to guarantee sequence.

**InventoryItem ID storage requirement:** The `InventoryItem.id` (Shopify's internal ID) for each variant is returned in the `productSet` mutation response. This ID must be stored on the CritterSupply `Listing` entity per variant, as it is required for all subsequent `inventorySetOnHandQuantities` calls. Without it, inventory sync is impossible without an additional Shopify API call per sync event.

**Location ID discovery:** The `locationId` for inventory mutations must be discovered at adapter setup time via the Locations query (requires `read_locations` scope). Do not hardcode it. Store the primary location ID on the `Marketplace` document entity for `SHOPIFY_US`. This requires querying a real Shopify development store before Cycle 35 implementation begins.

### 1.4 Order

A Shopify `Order` represents a completed customer purchase. For CritterSupply's Marketplaces BC, orders arrive via webhooks (not polling) and must be ingested into the Orders BC as first-class orders.

**Key properties:**
```json
{
  "id": "gid://shopify/Order/820982911946154508",
  "name": "#1001",
  "email": "customer@example.com",
  "createdAt": "2026-03-10T17:15:47-04:00",
  "financialStatus": "PAID",
  "fulfillmentStatus": "UNFULFILLED",
  "totalPriceSet": {
    "shopMoney": { "amount": "59.97", "currencyCode": "USD" }
  },
  "lineItems": {
    "edges": [{
      "node": {
        "id": "gid://shopify/LineItem/866550311766439020",
        "title": "Premium Salmon Dog Food",
        "quantity": 2,
        "sku": "DOG-SAL-5LB",
        "variantId": "gid://shopify/ProductVariant/808950810",
        "price": "29.99",
        "vendor": "NutriPaws",
        "requiresShipping": true
      }
    }]
  },
  "shippingAddress": {
    "firstName": "Jane",
    "lastName": "Doe",
    "address1": "123 Main St",
    "city": "Portland",
    "province": "OR",
    "zip": "97201",
    "countryCode": "US",
    "phone": "555-555-5555"
  },
  "transactions": [{
    "id": "gid://shopify/OrderTransaction/179259969",
    "gateway": "shopify_payments",
    "status": "SUCCESS",
    "amount": "59.97",
    "processedAt": "2026-03-10T17:15:50-04:00"
  }]
}
```

**Critical distinction:** The `financialStatus` is already `PAID` when we receive the order. Shopify collects payment at checkout — see [Section 6: The Payment Seam](#6-the-payment-seam).

**Payment holds:** Some Shopify orders arrive with `financialStatus: "pending"` when the payment is under fraud review. **Do not ingest these as CritterSupply orders.** The webhook handler must check `financialStatus === "paid"` before proceeding. Subscribe to `orders/updated` to re-trigger ingestion when a held order's status later transitions to `"paid"`. **Expiry policy for stuck pending orders (requires pre-Cycle 35 decision, see Section 11.3):** if an order remains in `"pending"` status for more than 72 hours (Shopify's typical fraud review window), discard it and log a structured warning. CS staff can manually evaluate if needed.

**Draft Order origin:** Orders in Shopify can be created manually by CS reps via Shopify's Draft Orders feature (for phone-in or goodwill orders). When a draft order is completed, Shopify fires the standard `orders/create` webhook with the same payload shape. The adapter does not need to distinguish draft-origin orders; they should be ingested identically. However, draft orders may have: zero shipping, manual discounts, or custom note fields that standard storefront orders don't carry. These additional fields don't affect ingestion but should be stored on the Order record's metadata for CS use.

**Stable identifier:** Shopify's `id` (GID format, e.g., `gid://shopify/Order/820982911946154508`) is the stable machine identifier. The `name` field (e.g., `#1001`) is the human-readable order number displayed in Shopify's admin and customer emails — it is NOT globally unique across stores. Always store and correlate by the numeric portion of the GID.

### 1.5 Fulfillment

A Shopify `Fulfillment` represents shipping work completed for one or more line items in an order. CritterSupply must push fulfillment status back to Shopify after our Fulfillment BC ships the order, so Shopify can send tracking notifications to the customer.

**Fulfillment flow:**
1. Shopify order arrives (webhook `orders/create`) → CritterSupply ingests it → Fulfillment BC ships the order
2. CritterSupply posts `POST /admin/api/{version}/fulfillments.json` with tracking info
3. Shopify marks the order as `Fulfilled` and sends the customer a shipping confirmation email

**Key fulfillment statuses:** `pending`, `open`, `success`, `cancelled`, `error`, `failure`

**Shipment tracking statuses:** `label_purchased`, `in_transit`, `out_for_delivery`, `delivered`, `failure`

---

## 2. Authentication

### 2.1 App Types — Which One for CritterSupply?

Shopify has three app types relevant to us:

| Type | Use Case | Auth Method | Token Expiry |
|------|----------|-------------|--------------|
| **Custom App** (Admin-created) | Single-merchant, owner-controlled | Admin-generated token | **Never expires** |
| **Custom App** (Dev Dashboard) | Single-merchant, developer-controlled | OAuth 2.0 | Offline: Never; Online: 24h |
| **Public App** | Multi-merchant distribution | OAuth 2.0 + installation | Offline: Never; Online: 24h |

**For CritterSupply's Marketplaces BC:** Use an **Admin-created Custom App** (created in Shopify's admin panel). This is the correct model because:
- We control the Shopify store; we are not distributing an app to other merchants
- Token never expires, simplifying Vault rotation (treat it as a long-lived secret that must be manually revocable)
- No OAuth flow required — the token is generated in the Shopify admin and stored in Vault at setup

### 2.2 Required Access Scopes

The Shopify adapter in the Marketplaces BC requires the following access scopes:

| Scope | Reason |
|-------|--------|
| `read_products`, `write_products` | Create, update, deactivate product listings |
| `read_product_listings` | Verify listing status |
| `read_inventory`, `write_inventory` | Update available inventory quantities |
| `read_locations` | Discover Location IDs for inventory updates |
| `read_orders`, `write_orders`, `read_all_orders` | Receive orders via webhook + retrieve order details |
| `read_fulfillments`, `write_fulfillments` | Push tracking info after shipment |
| `read_assigned_fulfillment_orders`, `write_assigned_fulfillment_orders` | Manage fulfillment order workflow |

> **Security principle:** Request only what you need. The adapter does NOT need `write_customers`, `write_discounts`, or `read_analytics` — these belong to Shopify-native tools, not CritterSupply's integration.

### 2.3 Making Authenticated Requests

All requests to the Admin API (REST or GraphQL) require the `X-Shopify-Access-Token` header:

```http
POST https://{store_name}.myshopify.com/admin/api/2026-01/graphql.json
X-Shopify-Access-Token: {access_token}
Content-Type: application/json
```

### 2.4 Vault Integration — Credential Shape

Per D6 decision, API credentials are stored in Vault. The `Marketplace` document entity for `SHOPIFY_US` would store a Vault path, and the adapter reads the credentials from Vault at startup (or per-request with caching).

**Vault secret shape for a Shopify Custom App:**
```json
{
  "store_domain": "critter-supply.myshopify.com",
  "access_token": "shpat_xxxxxxxxxxxxxxxxxxxxxxxx",
  "webhook_secret": "shpss_xxxxxxxxxxxxxxxxxxxxxxxx",
  "api_version": "2026-01"
}
```

Fields:
- `store_domain` — the `.myshopify.com` domain (permanent, never changes even if the Shopify store display name changes)
- `access_token` — the Admin API token; prefix `shpat_` indicates an admin-created Custom App token
- `webhook_secret` — the shared secret Shopify sends with each webhook for HMAC-SHA256 verification
- `api_version` — pinned API version string; stored in Vault so rotation doesn't require redeployment

**Token rotation without deployment:** The token never expires automatically, but must be regenerable if compromised. When a token is rotated in the Shopify admin, update the Vault secret at the path stored on the `Marketplace` document. The running adapter picks up the new credential on next Vault read (with reasonable TTL-based caching). No deployment or restart required.

---

## 3. GraphQL Admin API — The Preferred Path

### 3.1 Why GraphQL Over REST

Shopify has deprecated the REST Product API (`/admin/api/products.json`) for listing, creating, updating, and deleting products as of API version `2024-04`. The GraphQL Admin API is the forward-looking path for all product operations.

**Key advantages for CritterSupply:**
- `productSet` mutation replaces complete product state atomically — ideal for our external-catalog-is-source-of-truth model
- GraphQL bulk operations handle thousands of SKUs without pagination complexity
- Fine-grained field selection reduces payload size
- Better error handling via `userErrors` field on mutations (in addition to HTTP status codes)

**Endpoint:**
```
POST https://{store_name}.myshopify.com/admin/api/2026-01/graphql.json
```

### 3.2 GraphQL Error Handling Pattern

Unlike REST, GraphQL usually returns HTTP `200 OK` even when the operation failed. Errors appear in the response body under `errors` (schema-level errors) or `userErrors` (mutation-level business errors).

```csharp
// Always check both error paths
public sealed record ShopifyGraphQlResponse<T>
{
    public T? Data { get; init; }
    public IReadOnlyList<GraphQlError>? Errors { get; init; }  // Schema errors (auth, rate limits)
}

public sealed record GraphQlError
{
    public string Message { get; init; } = default!;
    public IReadOnlyList<string>? Locations { get; init; }
    public IReadOnlyDictionary<string, object>? Extensions { get; init; }
}

// Mutation responses contain UserErrors for business logic failures
public sealed record ProductSetPayload
{
    public ShopifyProduct? Product { get; init; }
    public IReadOnlyList<UserError>? UserErrors { get; init; }
}

public sealed record UserError
{
    public string Field { get; init; } = default!;  // e.g., "variants[0].price"
    public string Message { get; init; } = default!;
    public string Code { get; init; } = default!;   // e.g., "INVALID", "TOO_LONG"
}
```

---

## 4. API Operations

### 4.1 Create or Update a Product (Catalog Sync)

The `productSet` mutation is the recommended operation for syncing products from an external catalog system (our use case). It performs an upsert: creates the product if it doesn't exist, or replaces its complete state if it does.

```graphql
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
            inventoryItem {
              id
            }
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
```

**Variables for a multi-variant pet food product:**
```json
{
  "input": {
    "title": "Premium Salmon Dog Food",
    "descriptionHtml": "<p>Rich in Omega-3 fatty acids...</p>",
    "vendor": "NutriPaws",
    "productType": "Dog Food",
    "status": "ACTIVE",
    "tags": ["dog", "food", "salmon", "grain-free"],
    "options": [
      { "name": "Size", "values": [{ "name": "5lb" }, { "name": "15lb" }, { "name": "30lb" }] }
    ],
    "variants": [
      {
        "optionValues": [{ "optionName": "Size", "name": "5lb" }],
        "sku": "DOG-SAL-5LB",
        "price": "29.99",
        "compareAtPrice": "34.99",
        "barcode": "123456789001",
        "weight": 5.0,
        "weightUnit": "POUNDS",
        "requiresShipping": true,
        "taxable": true,
        "inventoryPolicy": "DENY",
        "inventoryManagement": "SHOPIFY"
      },
      {
        "optionValues": [{ "optionName": "Size", "name": "15lb" }],
        "sku": "DOG-SAL-15LB",
        "price": "69.99",
        "weight": 15.0,
        "weightUnit": "POUNDS",
        "requiresShipping": true,
        "taxable": true,
        "inventoryPolicy": "DENY",
        "inventoryManagement": "SHOPIFY"
      }
    ]
  }
}
```

> **Idempotency via `productSet`:** Unlike Stripe (which requires explicit idempotency keys), `productSet` is naturally idempotent when using SKU as the matching key. If you call it twice with the same SKU, the second call replaces the first. No duplicate products are created.

> **`compareAtPrice` source of truth:** The `compareAtPrice` field (shown as "34.99" in the example above) drives the "was $34.99, now $29.99" display on the Shopify storefront. This field must map to CritterSupply's **MSRP (Manufacturer's Suggested Retail Price)** or the product's list price — NOT the MAP (Minimum Advertised Price) floor. If no MSRP is defined in the Product Catalog, `compareAtPrice` should be omitted (null) rather than fabricated. Sending a false "compare at" price would constitute deceptive pricing on the Shopify storefront. The source of truth for `compareAtPrice` must be resolved with the Product Catalog team before Cycle 35 catalog sync is implemented.

### 4.2 Deactivate a Product Listing (Recall Cascade)

To deactivate a listing on Shopify (e.g., seasonal pause or recall cascade), update the product's `status` to `ARCHIVED`:

```graphql
mutation DeactivateListing($id: ID!) {
  productUpdate(input: { id: $id, status: ARCHIVED }) {
    product {
      id
      status
    }
    userErrors {
      field
      message
    }
  }
}
```

> **ARCHIVED vs. DRAFT vs. DELETE:** Use `ARCHIVED` for paused/ended listings — the product remains in Shopify's history, can be restored, and existing order line items still reference it correctly. `DRAFT` hides the product but keeps it in a pending state. `DELETE` is permanent and breaks historical order references. Our adapter should map: `ListingPaused` → `ARCHIVED`, `ListingEnded` → `ARCHIVED`, `ProductRecalled` → `ARCHIVED` (with highest priority).

### 4.3 Update Inventory (Availability Sync)

Inventory updates use the `inventorySetOnHandQuantities` mutation (GraphQL, preferred) or the REST `InventoryLevel` resource:

```graphql
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
```

**Variables:**
```json
{
  "input": {
    "reason": "correction",
    "setQuantities": [
      {
        "inventoryItemId": "gid://shopify/InventoryItem/49148385198",
        "locationId": "gid://shopify/Location/655441491",
        "quantity": 42
      }
    ]
  }
}
```

**Important:** We set **absolute on-hand quantity** (not a delta adjustment). The Marketplaces BC adapter reads the available quantity from our Inventory BC and pushes the absolute number to Shopify. This is safer than delta adjustments, which can drift if a message is lost or processed out of order.

### 4.4 Retrieve an Order (After Webhook)

When the `orders/create` webhook fires, the payload contains the full order. However, best practice is to use the webhook to get notified, then fetch the full order via GraphQL to ensure you have complete, current data (in case the webhook payload is stale due to rapid subsequent updates):

```graphql
query GetOrder($id: ID!) {
  order(id: $id) {
    id
    name
    email
    createdAt
    financialStatus
    fulfillmentStatus
    totalPriceSet { shopMoney { amount currencyCode } }
    lineItems(first: 50) {
      edges {
        node {
          id
          title
          sku
          quantity
          variantId: variant { id }
          price
          requiresShipping
        }
      }
    }
    shippingAddress {
      firstName lastName address1 address2
      city province zip countryCode phone
    }
    transactions(first: 5) {
      id gateway status amount processedAt
    }
  }
}
```

### 4.5 Create a Fulfillment (Ship-Back to Shopify)

After CritterSupply's Fulfillment BC ships the order, push tracking info to Shopify:

```graphql
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
    }
  }
}
```

**Variables:**
```json
{
  "fulfillment": {
    "lineItemsByFulfillmentOrder": [
      {
        "fulfillmentOrderId": "gid://shopify/FulfillmentOrder/1046000776",
        "fulfillmentOrderLineItems": [
          { "id": "gid://shopify/FulfillmentOrderLineItem/1058737527", "quantity": 2 }
        ]
      }
    ],
    "trackingInfo": {
      "company": "UPS",
      "number": "1Z999AA10123456784",
      "url": "https://www.ups.com/track?tracknum=1Z999AA10123456784"
    },
    "notifyCustomer": true
  }
}
```

> **Fulfillment Order prerequisite:** Shopify's modern fulfillment flow works through `FulfillmentOrder` resources (not directly through `Order`). When an order is placed, Shopify automatically creates one or more `FulfillmentOrder` records. The adapter must retrieve the `FulfillmentOrder` ID from the order before creating a fulfillment. This is fetched via the `order.fulfillmentOrders` connection.

---

## 5. Webhooks

### 5.1 Relevant Webhook Topics

The Marketplaces BC Shopify adapter needs subscriptions to these topics:

| Topic | Trigger | Our Action |
|-------|---------|-----------|
| `orders/create` | New order placed on Shopify | Ingest order into Orders BC (must verify `financialStatus === "paid"` first) |
| `orders/cancelled` | Order cancelled by customer or admin | Cancel CritterSupply order / release inventory |
| `orders/updated` | Order details edited | **Conservative:** Only react to structural changes in `line_items` or `shipping_address`. This webhook fires for ANY order modification (tag additions, note changes, etc.) — react only to meaningful changes. |
| `refunds/create` | Customer refund issued in Shopify | Capture refund data (Shopify transaction ID + amount) → publish `ShopifyRefundReceived` for audit trail. Do NOT attempt to process via Payments BC — Shopify already refunded. Returns BC integration wired in Cycle 36. |
| `products/update` | Product updated in Shopify admin directly | Log warning (Shopify is NOT our source of truth — this is an audit signal, not a sync trigger) |
| `fulfillments/update` | Fulfillment status changes | Update order tracking in CritterSupply |
| `inventory_levels/update` | Inventory changed in Shopify | Reconciliation signal (should match our push) |
| `app/uninstalled` | App removed from store | Deactivate `Marketplace` document for `SHOPIFY_US`, suspend all sync handlers, alert ops team (Backoffice notification + structured log alert). Pending in-flight fulfillments should still attempt push-back before suspension. Recovery requires manual re-installation and re-enabling the Marketplace document. |
| `customers/data_request` | GDPR: customer data request | **Mandatory** — must be subscribed. Note: we DO store customer PII (email, shipping address on Order records). Handler must query Orders BC for orders tied to this Shopify customer ID and return the data per GDPR Article 15. See [GDPR note](#gdpr-clarification) below. |
| `customers/redact` | GDPR: delete customer data | **Mandatory** — must be subscribed. Trigger data deletion workflow in Orders BC. |
| `shop/redact` | GDPR: delete shop data | **Mandatory** — must be subscribed |

> **GDPR mandatory webhooks:** Shopify requires all apps to subscribe to the three GDPR topics. Failure to handle them is a policy violation that can result in app removal. The Shopify adapter must implement handlers for these topics.

> **GDPR clarification — CritterSupply DOES store customer PII:** Despite earlier assumptions, CritterSupply's Orders BC stores the customer's email address and shipping address from every ingested Shopify order. The `customers/data_request` handler must query the Orders BC for all orders associated with the requesting Shopify customer ID and report that data to Shopify's data review endpoint. Responding with "no PII stored" is incorrect and a compliance risk. This requires coordination with Customer Identity BC and requires a formal data deletion procedure for `customers/redact`. This is a **legal/compliance concern** that must be resolved before the Shopify adapter goes to production.

### 5.2 Webhook Delivery Model

- **At-least-once delivery** — Shopify guarantees the webhook will eventually be delivered but does not guarantee exactly-once. Duplicate deliveries can occur.
- **No guaranteed ordering** — Webhooks for the same resource may arrive out of order (e.g., `orders/updated` before `orders/create`). Use the `X-Shopify-Triggered-At` header timestamp or the payload's `updated_at` field to resolve ordering ambiguity.
- **Retry behavior** — Shopify retries failed deliveries (non-2xx response) with exponential backoff for **48 hours**. After 48 hours without successful delivery, Shopify may disable the webhook subscription.
- **5-second response deadline** — Our endpoint must respond with HTTP 2xx within 5 seconds. If processing the order takes longer (e.g., publishing to RabbitMQ and waiting for confirmation), respond `200 OK` immediately and process asynchronously.

### 5.3 HMAC-SHA256 Webhook Verification

Every webhook includes the `X-Shopify-Hmac-Sha256` header, which is a Base64-encoded HMAC-SHA256 of the raw request body using the webhook secret (stored in Vault as `webhook_secret`).

```csharp
public static bool VerifyWebhookSignature(
    ReadOnlySpan<byte> rawBody,
    string hmacHeader,
    string webhookSecret)
{
    var secretBytes = Encoding.UTF8.GetBytes(webhookSecret);
    var computedHash = HMACSHA256.HashData(secretBytes, rawBody);
    var computedBase64 = Convert.ToBase64String(computedHash);

    // Constant-time comparison to prevent timing attacks
    return CryptographicOperations.FixedTimeEquals(
        Encoding.UTF8.GetBytes(computedBase64),
        Encoding.UTF8.GetBytes(hmacHeader));
}
```

> **Critical:** Read the raw bytes from the request body stream BEFORE any JSON deserialization. ASP.NET Core may normalize the JSON, breaking the HMAC. Enable `Request.EnableBuffering()` and read from `Request.Body` as a byte array.

### 5.4 Webhook Deduplication

Use the `X-Shopify-Event-Id` header as the deduplication key. Store processed event IDs in a short-lived store (Redis, Marten document, or a simple Postgres table) with a TTL of 48 hours (matching Shopify's retry window):

```csharp
public sealed record ShopifyWebhookHeaders
{
    public string Topic { get; init; } = default!;        // X-Shopify-Topic
    public string HmacSha256 { get; init; } = default!;  // X-Shopify-Hmac-Sha256
    public string ShopDomain { get; init; } = default!;  // X-Shopify-Shop-Domain
    public string WebhookId { get; init; } = default!;   // X-Shopify-Webhook-Id
    public string EventId { get; init; } = default!;     // X-Shopify-Event-Id
    public DateTimeOffset TriggeredAt { get; init; }     // X-Shopify-Triggered-At
}
```

### 5.5 Registering Webhooks

Webhooks are registered programmatically via the GraphQL Admin API when the Shopify channel is first configured (or via the Shopify admin UI). The Marketplaces BC adapter should register its webhook subscriptions on startup if they don't already exist:

```graphql
mutation RegisterWebhook($topic: WebhookSubscriptionTopic!, $endpoint: String!) {
  webhookSubscriptionCreate(
    topic: $topic
    webhookSubscription: {
      format: JSON
      callbackUrl: $endpoint
    }
  ) {
    webhookSubscription {
      id
      topic
      callbackUrl
    }
    userErrors {
      field
      message
    }
  }
}
```

---

## 6. The Payment Seam

This section describes the most significant architectural difference between Shopify and CritterSupply's own checkout flow. It warrants a dedicated ADR before Cycle 35 implementation begins.

### 6.1 The Problem

CritterSupply's Orders saga is designed around the following payment lifecycle:
1. Customer initiates checkout on CritterSupply Storefront
2. Payments BC **authorizes** payment (Stripe PaymentIntent created with `capture_method: manual`)
3. Inventory BC **reserves** stock
4. Fulfillment BC processes shipment
5. Payments BC **captures** payment upon fulfillment confirmation

**Shopify does not work this way.** When a customer places an order on Shopify:
1. Shopify collects and **captures** payment at checkout (Shopify Payments or third-party gateway)
2. Shopify sends `orders/create` webhook to CritterSupply — the `financialStatus` is already `PAID`
3. No further payment action is required or possible through our Payments BC

This means **the Payments BC authorization and capture steps are irrelevant for Shopify-originated orders.** The saga cannot treat a Shopify order the same as a Storefront order without modification.

### 6.2 Architectural Options

**Option A: Pre-Paid Order Entry Point**
Introduce a new command `PlaceMarketplaceOrder` that enters the Orders saga after the payment step — skipping Payments BC entirely. The saga records that payment was handled externally (by Shopify) and proceeds directly to inventory reservation and fulfillment.

```
Shopify orders/create webhook
  → MarketplaceOrderIngested event
  → Orders saga: InventoryReservationRequested
  → Fulfillment BC: FulfillmentRequested
  (Payments BC not involved)
```

*Pro:* Clean; Orders saga doesn't need to know about Shopify.
*Con:* Payments BC has no record of the transaction. Reconciliation requires cross-referencing Shopify and our order store.

**Option B: Synthesized Payment Event**
The Shopify adapter synthesizes a `PaymentCaptured` event using the Shopify transaction data, publishing it as if the Payments BC had processed it. The Orders saga sees a `PaymentCaptured` event and proceeds normally.

```
Shopify orders/create webhook
  → Shopify adapter creates: PaymentCaptured { Amount, Currency, ExternalReference: "shopify:{orderId}" }
  → Orders saga: standard flow (inventory → fulfillment)
```

*Pro:* Payments BC maintains a record; financial reporting is consistent.
*Con:* Artificial; the event didn't originate from our Payments BC. Could confuse future developers.

**Option C: Hybrid — Marketplace Payment Record**
Record the Shopify transaction in the Payments BC as a `MarketplacePayment` (read-only record, not a full PaymentIntent lifecycle), then proceed with Option A's saga flow.

*Pro:* Maintains a payment record for reporting; saga is clean.
*Con:* Payments BC needs to understand marketplace vs. standard payments.

### 6.3 Recommendation (Spike-Level)

**Option A is recommended for Cycle 35.** The simplest correct implementation. The Orders BC clearly records the channel origin (`ChannelCode: SHOPIFY_US`), which is sufficient for reconciliation. The Payments BC is not the source of truth for Shopify payments — Shopify is. Attempting to synthesize events (Option B) creates false data. Option C adds complexity without sufficient benefit at this stage.

**ADR Timing — Write Now, Not at Cycle 35 Planning:** The spike has done all the intellectual work required for the ADR. Options are documented, recommendation is clear. Deferring the ADR to Cycle 35 planning creates a risk: implementation begins before the decision is formalized, the ADR never gets written, and the codebase inherits an undocumented architectural choice. The ADR should be created immediately as a companion to this spike. Use the next available ADR number (check the ADR registry in `docs/decisions/`).

### 6.4 Unresolved Payment Edge Cases (Require Resolution Before Cycle 35)

**Payment Holds (`financialStatus: "pending"`):** Shopify places high-risk orders on hold pending fraud review. These orders fire `orders/create` with `financialStatus: "pending"` (not `"paid"`). The adapter must **not** ingest these. Subscribe to `orders/updated` to detect when the status transitions to `"paid"` and ingest at that point. Define the behavior: after how long do we discard a pending order if it never transitions?

**Chargebacks (`financialStatus: "CHARGED_BACK"`):** A customer can dispute a Shopify charge weeks after CritterSupply has fulfilled the order. Shopify changes the order's financial status and fires `orders/updated`. CritterSupply's fulfilled order has no mechanism to "un-fulfill." What is our response?
- Option: Store the chargeback status on the Order aggregate for CS visibility
- Option: Trigger a Returns-adjacent workflow (refund already happened at Shopify level)
- **This decision requires CS Operations input and may touch Returns BC.** Flag as a Cycle 35 pre-planning item.

**Reverse Cancel Propagation:** The spike handles `orders/cancelled` from Shopify → CritterSupply. The reverse direction — a CS agent cancelling a Shopify-originated order in CritterSupply — is undefined. If we do not propagate the cancellation to Shopify:
- The order stays "active" in Shopify's admin
- The customer receives no Shopify-native cancellation notification
- CS must manually cancel in Shopify's admin as a second step

**This workflow gap will burn CS staff repeatedly.** Resolution options:
1. Build reverse cancel in Cycle 35 (add `orderCancel` mutation call when CritterSupply cancels a Shopify order)
2. Explicitly defer and document a CS SOP: "All Shopify-originated cancellations must be initiated from the Shopify admin"

**This decision must be made at Cycle 35 planning, not during implementation.**

### 6.5 Shopify Refunds

When a customer requests a refund for a Shopify order, the refund is processed in Shopify's admin. Shopify sends a `refunds/create` webhook. The Marketplaces BC adapter must:
1. Handle the `refunds/create` webhook
2. Capture the Shopify refund transaction ID and amount
3. Publish `ShopifyRefundReceived` integration message for audit trail
4. **Do NOT attempt to issue the refund via our Payments BC** — Shopify already processed it

**Note on deferral scope (corrected from initial draft):** The `refunds/create` webhook handler should be implemented in Cycle 35 as a "capture and store" operation — not fully deferred. Deferring the entire handler means we have no record that Shopify issued a refund, creating a reconciliation gap from day one. The Shopify Payments payout will reflect the refund; our Order record won't. Full Returns BC integration (processing the refund through our Returns flow) is deferred to Cycle 36. The handler itself is not.

**Refund data structure (requires pre-Cycle 35 decision, see Section 11.3):** The captured `ShopifyRefundReceived` event data (Shopify refund transaction ID + amount) must be stored somewhere. Options: (a) on the Order aggregate as a `ShopifyRefunds` list — simplest, no new document; (b) a new Refund document in the Marketplaces BC; (c) an audit log table. This decision requires coordination with the Orders BC team before the `refunds/create` handler is implemented.

---

## 7. Rate Limits & Bulk Operations

### 7.1 GraphQL Admin API Rate Limits

Shopify uses a **calculated query cost** model (leaky bucket):

| Plan | Points/Second | Max Bucket | Variant Mutations/Day |
|------|--------------|------------|----------------------|
| Standard | 100 | 1,000 | No extra limit |
| Advanced Shopify | 200 | 2,000 | No extra limit |
| Shopify Plus | 1,000 | 10,000 | No extra limit |
| Enterprise (Commerce Components) | 2,000 | 20,000 | No extra limit |

> **Variant creation limit:** When a store has 50,000+ product variants, Shopify limits variant creation/update mutations to **1,000 per day** via `productCreate`, `productUpdate`, and `productVariantCreate`. Use `productSet` (which is exempt from this limit) for bulk initial sync.

**Query cost reference:**
- Scalar field: 0 points
- Object field: 1 point
- Mutation: 10 points base
- Connection (with `first: N`): proportional to N

**Rate limit headers in response:**
```json
{
  "extensions": {
    "cost": {
      "requestedQueryCost": 12,
      "actualQueryCost": 12,
      "throttleStatus": {
        "maximumAvailable": 1000.0,
        "currentlyAvailable": 988.0,
        "restoreRate": 100.0
      }
    }
  }
}
```

### 7.2 Handling Rate Limit Errors

When the rate limit is exceeded, Shopify GraphQL returns an error in the response body (not an HTTP 429 for GraphQL — it returns HTTP 200 with an error):

```json
{
  "errors": [{
    "message": "Throttled",
    "extensions": {
      "code": "THROTTLED",
      "documentation": "https://shopify.dev/api/usage/rate-limits"
    }
  }]
}
```

**Exponential backoff pattern for the adapter:**
```csharp
public async Task<T> ExecuteWithRetryAsync<T>(
    Func<Task<T>> operation,
    CancellationToken ct,
    int maxRetries = 5)
{
    for (int attempt = 0; attempt < maxRetries; attempt++)
    {
        var result = await operation();
        if (!IsThrottled(result)) return result;

        var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
        await Task.Delay(delay, ct);
    }
    throw new ShopifyRateLimitExceededException("Max retries exceeded");
}
```

### 7.3 Bulk Operations — Required for Initial Catalog Sync

For the **initial sync** of CritterSupply's catalog to Shopify (potentially thousands of products with multiple variants each), individual `productSet` mutations would exhaust rate limits quickly. Use **Bulk Operations** instead.

**Workflow:**
1. Serialize all products to a JSONL file (one JSON object per line, each describing a `productSet` input)
2. Upload JSONL to Shopify staging URL (obtained via `stagedUploadsCreate` mutation)
3. Submit `bulkOperationRunMutation` with the staged upload reference
4. Shopify processes asynchronously — subscribe to `bulk_operations/finish` webhook to be notified
5. Download result JSONL when complete; parse errors for any products that failed

```graphql
mutation RunBulkSync($stagedUploadPath: String!) {
  bulkOperationRunMutation(
    mutation: "mutation ProductSync($input: ProductSetInput!) { productSet(input: $input) { product { id } userErrors { field message } } }"
    stagedUploadPath: $stagedUploadPath
  ) {
    bulkOperation {
      id
      status
    }
    userErrors {
      field
      message
    }
  }
}
```

> **In API version 2026-01+, up to 5 bulk operations can run simultaneously per shop.** Earlier versions support only 1.

**When to use Bulk Operations vs. Individual Mutations:**

| Scenario | Use |
|----------|-----|
| Initial sync (100+ products) | Bulk Operations |
| Incremental product update (1 product changed) | `productSet` mutation |
| Inventory update (real-time, per-event) | `inventorySetOnHandQuantities` mutation |
| Recall cascade (urgent, 1 product) | `productUpdate` mutation (status: ARCHIVED) |
| Recall cascade (urgent, 100+ products) | Bulk Operations with priority monitoring |

---

## 8. Pet Supply Specific Gotchas

### 8.1 Restricted and Age-Restricted Products

Shopify does not natively enforce product-category restrictions at the API level (unlike Amazon, which has gated categories). However, Shopify store owners must comply with their local regulations. For CritterSupply, the following product types require attention:

- **Flea/tick treatments containing pesticides** (e.g., products with permethrin, fipronil): Some states restrict mail-order sale of pesticide products. Shopify will not block the listing, but the merchant is liable for compliance.
- **Prescription-only veterinary products**: Cannot be sold on Shopify without appropriate licensing. Our compliance metadata (`IsHazmat`, `HazmatClass`) should flag these for manual review before submitting to Shopify.
- **Supplement products with health claims**: Shopify prohibits certain health-related marketing claims for supplements (in alignment with FTC/FDA guidelines). Review product descriptions before sync.
- **Controlled substances**: Not applicable for standard pet supply, but some flea/tick and sedation products qualify. Shopify will remove these listings if flagged.

**Adapter behavior:** Before submitting a product to Shopify, the adapter should check `IsHazmat` and `HazmatClass`. If `HazmatClass` is `Class 3` (flammable liquids, common in aerosol sprays) or higher, log a compliance warning and optionally require manual approval before auto-submitting.

### 8.2 Weight-Based Shipping

Large pet food bags (15lb, 30lb, 40lb bags) significantly affect shipping costs and carrier selection. Shopify uses the `weight` and `weightUnit` fields on `ProductVariant` for shipping rate calculations.

**Critical gotcha:** Shopify's shipping rate calculation uses the **variant weight at the time of order**. If we update a variant's weight after a customer has added it to their cart, the old weight is used for that cart's shipping calculation. Weight changes should be synchronized promptly when our Product Catalog data changes.

**Weight units:** Shopify accepts `GRAMS`, `KILOGRAMS`, `OUNCES`, `POUNDS`. Use `POUNDS` for CritterSupply (our catalog stores weights in pounds; no conversion needed).

### 8.3 Metafields for Compliance Data

Shopify supports custom **metafields** on products and variants, which are our mechanism for storing compliance and pet-specific data that doesn't map to Shopify's standard fields:

```graphql
mutation SetProductMetafield($productId: ID!, $namespace: String!, $key: String!, $value: String!) {
  productUpdate(input: {
    id: $productId
    metafields: [
      { namespace: $namespace, key: $key, value: $value, type: "single_line_text_field" }
    ]
  }) {
    product { id metafields(first: 5) { edges { node { namespace key value } } } }
    userErrors { field message }
  }
}
```

**Recommended metafield namespace and keys for CritterSupply:**
```
Namespace: critter_supply
Keys:
  - is_hazmat: "true" | "false"
  - hazmat_class: "Class 3" | "Class 8" | etc.
  - life_stage: "Puppy" | "Adult" | "Senior" | "All Life Stages"
  - breed_size: "Small" | "Medium" | "Large" | "Giant"
  - prescription_required: "true" | "false"
```

These metafields can be used by Shopify themes/apps to display filtering options, compliance warnings, or detailed product specifications.

> **Metafield display dependency:** Pushing metafields to Shopify is a write-only operation unless the Shopify storefront theme reads and displays them. Metafields are P1 for Cycle 35 (per PO decision) — but their customer-facing value depends on the Shopify theme being configured to surface them (e.g., as a compliance badge or filter option). Coordinate with Shopify store configuration before Cycle 35. If no theme customization is scoped for Cycle 35, the metafields are still worth pushing: Shopify admin users can see them, and they can be surfaced when theme customization is added later.

### 8.4 Inventory Policy — Never Oversell Pet Food

Shopify's `inventoryPolicy` on a `ProductVariant` controls what happens when the inventory runs to zero:
- `DENY`: Customer cannot purchase the variant if out of stock (**recommended for all CritterSupply products**)
- `CONTINUE`: Customer can purchase even when inventory shows 0 (overselling allowed)

**Always use `DENY` for CritterSupply.** Pet food and supply orders are usually single-purpose (for a specific pet's dietary need). An oversold order that can't be fulfilled is a high-friction customer experience — they trusted us for their pet's regular food supply.

### 8.5 Subscription Products (Future Consideration)

Pet food has a high repeat-purchase rate. Shopify supports selling subscription products through Shopify's **Selling Plans API** (via apps like Recharge or native Shopify Subscriptions for Plus merchants). This is NOT in scope for Cycle 35 but should be noted: when CritterSupply designs its Shopify product sync, avoid model choices that would conflict with later subscription enablement. Specifically: keeping a clean 1:1 SKU-to-variant mapping (rather than bundling) leaves the door open for selling plan integration.

---

## 9. Category Mapping Analysis

### 9.1 Shopify's Categorization Model vs. Amazon/Walmart

A key question for the Marketplaces BC's `CategoryMapping` document store is whether Shopify requires the same kind of rigid category mapping as Amazon and Walmart.

**The answer: Shopify's categorization is significantly looser.**

| Platform | Categorization Model | Mapping Complexity |
|----------|---------------------|-------------------|
| **Amazon** | Rigid taxonomy with `product_type` nodes and browse nodes; required for indexing | HIGH — must map CritterSupply categories to Amazon's taxonomy exactly |
| **Walmart** | Similar to Amazon; category required for listing creation | HIGH |
| **eBay** | Category tree; category ID required | MEDIUM |
| **Shopify** | `product_type` (free text) + custom/smart collections (merchant-defined) | LOW |

**Shopify's two categorization mechanisms:**
1. **`product_type`**: A free-text field on the product. Used for Shopify's default admin filtering. No validation against a fixed taxonomy. We can set this to our internal category name verbatim (e.g., "Dog Food", "Cat Toys", "Bird Feeders").
2. **Collections**: Products are organized into collections manually (Custom Collections) or by rules (Smart Collections). Collections are created and managed in Shopify's admin — we do NOT need to programmatically assign products to collections for basic listing functionality. Collections are an administrative/merchandising concern, not a listing requirement.

### 9.2 Implication for the `CategoryMapping` Document Store

For Shopify, the `CategoryMapping` document is effectively a passthrough. Our adapter should:
- Map CritterSupply's internal category (e.g., `Dogs > Food > Dry Food`) → Shopify `product_type` = `"Dog Food"` (the top-level category name)
- Optionally: map to Shopify's **Standard Product Types** (a curated list introduced in 2023) for better Google Shopping integration — these are controlled vocabulary values Shopify recommends but does not enforce

**Standard Product Type examples for pet supply:**
- `"Animals & Pet Supplies > Pet Supplies > Dog Supplies > Dog Food"`
- `"Animals & Pet Supplies > Pet Supplies > Cat Supplies > Cat Food"`

Using Standard Product Types (which follow the Google Product Taxonomy) benefits SEO and Google Shopping integration. The adapter should map to these where available, falling back to our internal category name.

### 9.3 Shopify Collections — Catalog Operations Gap

**This is an unresolved operational question, not a technical one.**

Shopify organizes products into **Collections** for storefront navigation (e.g., Shop → Dog Food → Dry Food). Collections are how customers browse — not `product_type`, which is used mainly for admin filtering. There are two types:
- **Custom Collections** — Manually curated lists of products
- **Smart Collections** — Rule-based automatic grouping (e.g., "all products where `product_type = Dog Food`")

**The gap:** The spike covers `product_type` mapping but does not address collection management. For a pet supply store with hundreds of SKUs across categories (dog food, cat toys, bird feeders, reptile supplies), manually managing which collections each product belongs to is a significant operational burden.

**Resolution required before Cycle 35 implementation:**

Two approaches exist:
1. **Programmatic via API**: Use the GraphQL `collectionAddProducts` mutation to assign products to custom collections during catalog sync. Requires that collections be pre-created in the Shopify admin and their IDs stored in our `CategoryMapping` document store.
2. **Smart Collections (recommended)**: Create smart collections in Shopify admin that auto-include products based on `product_type` rules. Example: Smart Collection "Dog Food" matches all products where `product_type contains "Dog Food"`. Zero API calls needed from our adapter — products appear automatically in the correct collection as they're synced.

**Recommendation:** Smart Collections for Cycle 35. Zero implementation overhead; pure Shopify admin configuration. Document this in the Shopify store setup guide for ops team. Programmatic collection assignment can be added in Cycle 36 if Smart Collections don't meet merchandising needs.

**Action item:** Ops team / catalog manager must configure Smart Collections in the Shopify development store before Cycle 35 testing begins. This is not engineering work.

### 9.4 Conclusion

Shopify's category mapping is a **special, simpler case** in our `CategoryMapping` document store. The `CategoryMapping` entity for `SHOPIFY_US` may only need to store a mapping from our internal category to a `product_type` string. Full taxonomy tree navigation (as Amazon requires) is not applicable. This is a meaningful simplification compared to the Amazon adapter.

---

## 10. API Version Pinning Policy

### 10.1 Shopify's Versioning Cadence

Shopify releases new API versions **quarterly** (e.g., `2026-01`, `2026-04`, `2026-07`, `2026-10`). Each version is supported for **12 months** after its release date. After 12 months, the version is deprecated and Shopify automatically falls forward to the oldest supported version.

**Current recommended version:** `2026-01` (as of this spike, March 2026)

**Version deprecation timeline:**
```
2025-01 — Deprecated April 2026
2025-04 — Deprecated July 2026
2025-07 — Deprecated October 2026
2025-10 — Deprecated January 2027
2026-01 — Deprecated April 2027
```

### 10.2 Pinning Strategy for CritterSupply

1. **Store the API version in Vault** (as `api_version` in the Shopify credential secret) — not hardcoded in the adapter. This allows version updates without deployment.
2. **Test against the new version** before updating Vault — use Shopify's version-specific endpoint to verify no breaking changes affect our operations.
3. **Set a calendar reminder** when pinning a version: schedule a review 6 months before deprecation.
4. **Never rely on "fall-forward"** — Shopify's automatic fall-forward can silently break integrations if the old version used deprecated mutations. Always upgrade explicitly.

### 10.3 Version-Specific Considerations

**Product Model change (2024-04):** REST Product API deprecated. CritterSupply's adapter must use GraphQL for all product operations. This spike is written against GraphQL exclusively for this reason.

**Bulk Operations (2026-01):** API version 2026-01 raised the concurrent bulk operations limit from 1 to 5. Pin to `2026-01` or later to take advantage of this for large catalog syncs.

---

## 11. Scope for Cycle 35 Shopify Adapter

This section is the explicit scope boundary for the Cycle 35 implementation of the Shopify adapter within the Marketplaces BC. Items marked **In Scope** must be delivered; items marked **Deferred** are noted for future cycles.

### 11.1 In Scope — Cycle 35

| Feature | Description | Priority |
|---------|-------------|---------|
| **Credential configuration** | Store Vault path on `Marketplace` document; adapter reads Shopify credentials from Vault on startup | P0 |
| **Catalog sync (incremental)** | Subscribe to `ProductContentUpdated`, `ProductStatusChanged`, `ProductDiscontinued` from Product Catalog BC; call `productSet` mutation per event | P0 |
| **Product deactivation** | Handle `ListingPaused`, `ListingEnded`, recall cascade → `productUpdate(status: ARCHIVED)` | P0 |
| **Order ingestion** | Receive `orders/create` webhook → validate HMAC → publish `MarketplaceOrderReceived` to Orders BC | P0 |
| **Order cancellation handling** | Receive `orders/cancelled` webhook → publish `MarketplaceOrderCancelled` to Orders BC | P0 |
| **Fulfillment push-back** | Subscribe to `ShipmentDispatched` from Fulfillment BC → call Shopify fulfillment create mutation | P0 |
| **Inventory sync (reactive)** | Subscribe to `InventoryLevelChanged` from Inventory BC → call `inventorySetOnHandQuantities` | P1 |
| **Webhook registration** | Register all required webhook subscriptions on startup; idempotent (don't duplicate if already registered) | P0 |
| **HMAC verification** | Verify all incoming webhooks before processing | P0 (security) |
| **Webhook deduplication** | Use `X-Shopify-Event-Id` to detect and discard duplicate deliveries | P1 |
| **GDPR mandatory webhooks** | Implement `customers/data_request`, `customers/redact`, `shop/redact` handlers. **Note:** CritterSupply DOES store PII (customer email + shipping address on Orders). Handlers must query Orders BC, not respond with "no PII stored." Coordinate with Customer Identity BC for data deletion. | P0 (policy) |
| **Refund capture** | `refunds/create` webhook → capture Shopify transaction ID + amount → publish `ShopifyRefundReceived` for audit trail and reconciliation. Full Returns BC integration wired in Cycle 36. | P1 |
| **Shopify InventoryItem ID storage** | Store the `InventoryItem.id` returned in `productSet` response per variant on the `Listing` entity. Required for inventory sync mutations. Without it, every inventory sync requires an additional API lookup. | P0 |
| **Rate limit handling** | Exponential backoff on `THROTTLED` errors; use bulk operations for >100 products | P1 |
| **Error reporting** | `MarketplaceSubmissionRejected` event when `userErrors` returned; include field-level detail | P1 |

### 11.2 Deferred — Post Cycle 35

| Feature | Reason for Deferral |
|---------|---------------------|
| **Initial catalog bulk sync** | Use bulk operations for first-time population of Shopify store. Deferred because Cycle 35 starts with an empty Shopify test store; incremental sync is sufficient. Deliver in Cycle 36. |
| **Full refund/returns integration** | `refunds/create` webhook → full Returns BC integration. Depends on Returns BC Phase 3 Exchange workflow. Capture-and-store handler (`ShopifyRefundReceived`) is in scope for Cycle 35; processing through Returns BC is Cycle 36. |
| **Reverse cancel propagation** | CritterSupply cancel → Shopify `orderCancel` mutation. Decision on this (build now vs. CS SOP) must be made at Cycle 35 planning. If deferred, document CS SOP explicitly. |
| **Chargeback handling** | `orders/updated` for `financialStatus: CHARGED_BACK`. Requires CS Operations input and possible Returns BC involvement. Cycle 36 minimum. |
| **Multi-location inventory** | CritterSupply has a single warehouse at launch. Multi-location inventory sync deferred until Inventory BC supports multiple locations. |
| **Customer data lookup** | Shopify order ID ↔ CritterSupply order ID lookup in Backoffice. Basic correlation stored on Order aggregate; Backoffice search feature deferred to Backoffice Phase 2. |
| **Subscription products** | Selling Plans API integration. Requires marketing/business decision on subscription model. |
| **Programmatic collection management** | Smart Collections (recommended in Section 9.3) handle this without API calls. Programmatic collection assignment can be added if Smart Collections are insufficient. |
| **Price sync** | Separate `PriceChanged` event handler (in addition to full `productSet` sync). Can be achieved with incremental `productSet` in Cycle 35; dedicated price sync endpoint deferred. |
| **Shopify analytics read** | Reading order analytics from Shopify. Backoffice dashboard for Shopify channel activity. Deferred to Backoffice Phase 2. |
| **Circuit-breaker** | Auto-suspend adapter after repeated failures. Basic retry/backoff in Cycle 35; circuit-breaker (with `ShopifyAdapterCircuitOpen` event + Backoffice alert) in Cycle 36. |

### 11.3 Pre-Cycle 35 Requirements (Must Be Resolved Before Planning Closes)

These items are not implementation tasks — they are decisions, configurations, or research items that must be resolved before the engineering team writes the first line of Cycle 35 code:

| Item | Owner | Deadline |
|------|-------|---------|
| Write Payment Seam ADR (Option A formalized) | UX Engineer / Principal Architect | Before Cycle 35 planning |
| Shopify development store set up; Location ID discovered | Engineering | Before Cycle 35 sprint day 1 |
| `compareAtPrice` source of truth resolved (MSRP vs. MAP; see Section 4.1) | Product Owner + Product Catalog team | Before catalog sync implementation |
| Pending order expiry policy defined (payment holds; see Section 1.4) | Product Owner + CS Operations | Before Cycle 35 planning |
| Reverse cancel propagation decision (build now vs. CS SOP; see Section 6.4) | Product Owner + CS Operations | Before Cycle 35 planning |
| Smart Collections configured in Shopify dev store (see Section 9.3) | Ops / Catalog Manager | Before Cycle 35 testing |
| GDPR compliance procedure — data request + deletion formal process | Legal / Compliance + Customer Identity BC | Before production launch |
| Refund capture data structure decided (Order aggregate vs. new document; see Section 6.5) | Engineering + Orders BC team | Before `refunds/create` implementation |

---

## 12. Product Owner Review & Feedback

**Review Date:** 2026-03-13
**Reviewer:** Product Owner (Agent)
**Status:** ✅ Approved as Research Foundation — Two review rounds complete

---

### Round 1 Feedback (Initial Draft)

**✅ Confirmed Decisions**

1. **Option A (Pre-Paid Order Entry Point) endorsed** for the Payment Seam. Orders BC `ChannelCode` field provides sufficient traceability. A dedicated ADR must be created immediately (not deferred to Cycle 35 planning).

2. **`productSet` over `productCreate`/`productUpdate`** confirmed. This is the architectural decision that keeps Shopify from diverging from our catalog source of truth — must be prominent in the implementation guide.

3. **GDPR mandatory webhooks** elevated to P0. Non-negotiable policy requirement.

4. **`inventoryPolicy: DENY`** — strong agreement. Hard constraint in the adapter, not a configuration option.

**⚠️ Round 1 Corrections (incorporated into main document)**

5. Shopify transaction ID must be stored on CritterSupply Order record for financial reconciliation.

6. `orders/updated` must filter conservatively — only react to structural changes in `line_items` or `shipping_address`, not all modifications.

7. Fulfillment push-back must be synchronous in the Wolverine handler for `ShipmentDispatched` — not deferred to a background job.

8. `is_hazmat` and `prescription_required` metafields elevated to P1 for Cycle 35.

9. Option A holds regardless of third-party payment gateway — `financialStatus: PAID` is our signal; gateway identity is for reconciliation only.

10. Circuit-breaker behavior: basic retry/backoff in Cycle 35; `ShopifyAdapterCircuitOpen` circuit-breaker in Cycle 36.

---

### Round 2 Feedback (Final Draft Review)

**🔴 Critical Gaps Found and Incorporated**

**Gap 1: Dual-Decrement Problem Was Understated**
The spike originally mentioned that "Shopify does its own reservation" but didn't trace the race condition. The inventory sync section now explicitly documents the dual-decrement sequence and requires that Wolverine handler ordering enforce: inventory reservation COMMITTED → then inventory push to Shopify. Engineering must see this before writing handler code.

**Gap 2: `app/uninstalled` Webhook Had No Business Response**
"Alert + disable adapter" was insufficient. Section 5.1 now specifies: deactivate `Marketplace` document for `SHOPIFY_US`, suspend sync handlers, alert ops team via Backoffice notification + structured log, allow in-flight fulfillments to complete before suspension, and require manual re-installation + document re-enablement for recovery.

**Gap 3: Draft Orders Were Missing**
When CS reps create Shopify draft orders (phone-in, goodwill orders), completion fires `orders/create`. Section 1.4 now notes this and confirms the payload is structurally identical — ingest the same way. Additional fields (zero shipping, manual discounts, notes) are stored for CS context.

**Gap 4: Reverse Cancel Propagation Is Undefined**
A CS agent cancelling a Shopify-originated order in CritterSupply does NOT automatically cancel it in Shopify. Section 6.4 now defines this as an unresolved workflow gap and requires a business decision (build reverse cancel in Cycle 35 vs. CS SOP) before Cycle 35 planning closes. This will "burn CS staff repeatedly" if left undefined — PO language preserved.

**Gap 5: Shopify Collections Not Addressed**
Section 9.3 now explicitly addresses the Collections gap and recommends Smart Collections (zero API implementation overhead; Shopify admin configuration only). Action item placed on ops/catalog manager to configure before Cycle 35 testing.

**Gap 6: `compareAtPrice` Source of Truth Undefined**
Section 4.1 now requires resolution of the `compareAtPrice` source (MSRP vs. MAP) before catalog sync is implemented. False "compare at" pricing is a deceptive advertising risk. Pre-Cycle 35 requirements table added.

**Gap 7: GDPR "No PII Stored" Claim Was Incorrect**
Section 5.1 webhook table and Section 12 now explicitly state: CritterSupply DOES store customer PII (email, shipping address on Order records). The `customers/data_request` handler must query Orders BC and report actual data. Legal/compliance must be consulted before production launch.

**Scope Table Corrections**

- **`refunds/create`** corrected: NOT fully deferred. Capture-and-store handler (`ShopifyRefundReceived`) is Cycle 35 P1. Full Returns BC integration remains Cycle 36. A reconciliation gap from day one is unacceptable.
- **Shopify InventoryItem ID storage** added as P0 — without it, inventory sync is broken from the start.
- **GDPR handlers** corrected: not "no PII stored" — actual data reporting required.
- **Pre-Cycle 35 requirements table** added as Section 11.3.

**Payment Seam ADR Timing Corrected**
Original spike said "ADR before Cycle 35 planning." PO overruled: ADR should be written NOW. The spike has completed all the intellectual work. Deferring creates risk that implementation begins before the decision is formalized.

**Fulfillment Push-Back Needs More Prominence**
The FulfillmentOrder prerequisite (fulfillmentCreate takes a FulfillmentOrder ID, not an Order ID) is the most dangerous implementation gotcha in the document. It is now in a dedicated prominent note in Section 4.5.

---

### PO Final Sign-Off

> "Round 2 feedback has been incorporated into the document. The dual-decrement problem is now properly documented. The GDPR correction is accurate and important. The Collections section (Smart Collections recommendation) resolves the operational concern without adding engineering complexity — good call. The scope table corrections on `refunds/create` and InventoryItem ID storage are right. The pre-Cycle 35 requirements table is what I wanted.
>
> On ADR timing: write it now. We've made the decision here. Formalizing it in an ADR document is 30 minutes of work and eliminates the risk of implicit decisions. Do not defer.
>
> The document is approved as a research foundation. Not approved as an implementation spec — that comes at Cycle 35 planning with the engineering team present. Three things must happen before Cycle 35 planning closes: (1) Payment Seam ADR written, (2) Shopify development store live with Location ID discovered, (3) cancel propagation decision made with CS Operations input."
>
> — Product Owner

---

## References

- **[Shopify GraphQL Admin API Reference](https://shopify.dev/docs/api/admin-graphql)** (external)
- **[Shopify REST Admin API Reference](https://shopify.dev/docs/api/admin-rest)** (external)
- **[Shopify Webhooks Overview](https://shopify.dev/docs/apps/build/webhooks)** (external)
- **[Shopify New Product Model (GraphQL)](https://shopify.dev/docs/api/admin/migrate/new-product-model)** (external)
- **[Shopify GraphQL Bulk Operations](https://shopify.dev/docs/api/usage/bulk-operations/queries)** (external)
- **[Shopify Rate Limits Reference](https://shopify.dev/docs/api/usage/limits)** (external)
- **[Shopify Access Scopes Reference](https://shopify.dev/docs/apps/auth/admin-app-access-tokens)** (external)
- **Companion document:** `docs/examples/shopify/README.md` — reference code examples
- **Prior art:** `docs/planning/spikes/stripe-api-integration.md` — Stripe spike (same format)
- **Related architecture:** `docs/planning/catalog-listings-marketplaces-cycle-plan.md` — Cycles 32–35
- **Related architecture:** `docs/planning/catalog-listings-marketplaces-evolution-plan.md` — Phase 2 design
- **Related architecture:** `docs/planning/catalog-listings-marketplaces-glossary.md` — Ubiquitous language
- **ADR candidate:** Shopify Payment Seam architectural decision (to be created at Cycle 35 planning)
