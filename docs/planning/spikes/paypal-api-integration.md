# PayPal API Integration Research Spike

**Date:** 2026-03-13
**Status:** Research Complete — Awaiting Implementation Planning
**Author:** UX Engineer
**Reviewed By:** Product Owner (see [Product Owner Feedback](#product-owner-feedback) section)
**Purpose:** Understand PayPal's API patterns, paradigms, and integration requirements so that CritterSupply can offer a complementary PayPal checkout option alongside Stripe for its (fake) customers.

---

## Executive Summary

This document provides a comprehensive overview of PayPal's payment API surface — how it works, where it diverges from Stripe, and what it requires for integration into CritterSupply's Payments bounded context.

**Key Findings:**

- PayPal's primary checkout model uses a **buyer-redirect / overlay approval flow** that is fundamentally different from Stripe's token-based flow. This is the single most important architectural difference for the CritterSupply Payments BC.
- PayPal's authorization window varies by payment source: **3 days (card-funded), 29 days (PayPal balance), or async settlement (eCheck/ACH — not a traditional hold model)** — compared to Stripe's uniform 7 days. Use PayPal's returned `expiration_time` value; do not compute it independently.
- PayPal's authentication uses **OAuth 2.0 Client Credentials** (access tokens that expire every ~8 hours), unlike Stripe's simple static API key — token refresh logic must be built before anything else.
- PayPal's webhook verification is **certificate-based (RSA-SHA256 + CRC32)**, significantly more complex than Stripe's HMAC-SHA256 approach.
- **`IPaymentGateway` interface should remain unchanged** (PO Decision). PayPal's order-creation step lives in a dedicated PayPal controller endpoint outside the gateway abstraction. See [IPaymentGateway Extension Recommendations](#ipaymentgateway-extension-recommendations).
- PayPal offers unique business features — **Pay Later, Buyer Protection, Seller Protection** — that Stripe does not, providing real differentiation value for CritterSupply's reference architecture.
- **PayPal's one-tap approval from saved accounts is disproportionately valuable on mobile** — a critical channel for pet supply retailers where re-entering payment credentials is a major checkout friction point.
- PayPal's developer tooling requires more setup than Stripe's (no CLI equivalent — use ngrok or the PayPal Sandbox Simulator), but the Sandbox environment itself is robust.
- **PayPal should be positioned as complementary, not a replacement for Stripe.** Stripe remains the developer-experience reference; PayPal demonstrates real-world multi-gateway scenarios and addresses customers who prefer not to type card numbers.
- **`PAYMENT.CAPTURE.PENDING`** (eCheck/ACH transactions) introduces an unresolved Order saga state gap — see [Known Limitations and Phase 2 Scope](#known-limitations-and-phase-2-scope).
- **Abandoned PayPal orders** (buyer clicks Pay with PayPal then cancels) create dangling order objects that require lifecycle handling — see [Abandoned Order Cleanup](#abandoned-order-cleanup).

---

## Background: How the Current Payments BC Works

Before diving into PayPal-specific details, this section summarizes the Payments BC as it stands today — the system PayPal will integrate into. *(See the full analysis in Product Owner brief included in [Appendix A](#appendix-a-product-owner-brief).)*

### Current Inbound Messages

| Message | From | Purpose |
|---|---|---|
| `PaymentRequested` | Orders BC | One-phase immediate capture |
| `AuthorizePayment` | Orders BC | Two-phase: hold funds |
| `CapturePayment` | Orders BC | Two-phase: collect held funds |
| `RefundRequested` | Orders BC | Initiate a refund |

### Current Outbound Integration Messages

| Message | To | Purpose |
|---|---|---|
| `PaymentAuthorized` | Orders saga | Funds held successfully |
| `PaymentCaptured` | Orders saga, CE | Funds collected |
| `PaymentFailed` | Orders saga | Payment rejected |
| `RefundCompleted` | Orders saga | Refund processed |
| `RefundFailed` | Orders saga | Refund rejected |

### Current `IPaymentGateway` Interface

```csharp
public interface IPaymentGateway
{
    Task<GatewayResult> AuthorizeAsync(decimal amount, string currency, string paymentMethodToken, CancellationToken ct);
    Task<GatewayResult> CaptureAsync(decimal amount, string currency, string paymentMethodToken, CancellationToken ct);
    Task<GatewayResult> CaptureAuthorizedAsync(string authorizationId, decimal amount, CancellationToken ct);
    Task<GatewayResult> RefundAsync(string transactionId, decimal amount, CancellationToken ct);
}

public sealed record GatewayResult(bool Success, string? TransactionId, string? FailureReason, bool IsRetriable);
```

The current implementation in production: `StubPaymentGateway` (simulated responses only). Stripe example code exists in `docs/examples/stripe/` but is not yet wired into the running system.

---

## Core PayPal Concepts

### 1. Authentication: OAuth 2.0 Client Credentials

PayPal uses **OAuth 2.0 Client Credentials grant** — not a simple API key like Stripe. This has meaningful implications for implementation.

```csharp
// Step 1: Get Access Token (MUST refresh when expired)
POST https://api-m.sandbox.paypal.com/v1/oauth2/token
Authorization: Basic {Base64(client_id:client_secret)}
Content-Type: application/x-www-form-urlencoded

Body: grant_type=client_credentials
```

```json
// Response: Access token expires in ~8 hours (31,668 seconds)
{
  "access_token": "A21AAFEpH4PsADK7...",
  "token_type": "Bearer",
  "app_id": "APP-80W284485P519543T",
  "expires_in": 31668,
  "nonce": "2020-04-03T15:35:36ZaYZlGvEkV4yVSz8g6bAKFoGSEzuy3CQcz3ljhibkOHg"
}
```

**Gotcha:** The access token EXPIRES. Unlike Stripe's static secret key, PayPal's token must be refreshed before expiry. Implementation must cache the token and proactively refresh it (typically when less than 60 seconds of TTL remain). A background `IHostedService` or lazy re-authentication strategy is required.

**Sandbox vs Production Hosts:**
- Sandbox: `https://api-m.sandbox.paypal.com`
- Production: `https://api-m.paypal.com`

### 2. The PayPal Order: The Central Abstraction

PayPal's **Orders API v2** (`/v2/checkout/orders`) is the current recommended API surface for payment processing. The `Order` object is PayPal's equivalent of Stripe's `PaymentIntent`.

```csharp
public sealed record PayPalOrder
{
    public string Id { get; init; } = default!;        // e.g. "5O190127TN364715T"
    public string Status { get; init; } = default!;    // See statuses below
    public string Intent { get; init; } = default!;    // "CAPTURE" or "AUTHORIZE"
    public PayPalPurchaseUnit[] PurchaseUnits { get; init; } = [];
    public PayPalPaymentSource? PaymentSource { get; init; }
    public PayPalLink[] Links { get; init; } = [];
    public DateTimeOffset CreateTime { get; init; }
    public DateTimeOffset UpdateTime { get; init; }
}
```

**Order Statuses:**

| Status | Meaning |
|---|---|
| `CREATED` | Order created server-side, awaiting buyer approval |
| `SAVED` | Buyer has saved order (rarely used) |
| `APPROVED` | Buyer approved payment (ready to capture or authorize) |
| `VOIDED` | Order cancelled before completion |
| `COMPLETED` | Order fully captured (funds collected) |
| `PAYER_ACTION_REQUIRED` | Additional buyer action needed (3D Secure, etc.) |

### 3. Purchase Units and Items

PayPal's order model is **purchase-unit-centric**, designed for merchants potentially selling across multiple sellers or currencies. For CritterSupply (single seller), there will always be exactly one purchase unit.

```json
// Purchase unit within an order
{
  "amount": {
    "currency_code": "USD",
    "value": "19.99",
    "breakdown": {
      "item_total": { "currency_code": "USD", "value": "15.99" },
      "shipping": { "currency_code": "USD", "value": "4.00" }
    }
  },
  "items": [
    {
      "name": "Premium Dog Food",
      "unit_amount": { "currency_code": "USD", "value": "15.99" },
      "quantity": "1",
      "category": "PHYSICAL_GOODS"
    }
  ],
  "shipping": {
    "name": { "full_name": "John Doe" },
    "address": {
      "address_line_1": "123 Main St",
      "admin_area_2": "San Jose",
      "admin_area_1": "CA",
      "postal_code": "95131",
      "country_code": "US"
    }
  }
}
```

> ⚠️ **Seller Protection Note:** PayPal's Seller Protection requires the `shipping` object to be populated with the buyer's address. Without this, transactions are not eligible for seller protection against item-not-received disputes. This creates a cross-BC dependency: Payments BC may need shipping address data from the Order/Fulfillment BC, which it doesn't currently receive via `PaymentRequested`.

### 4. Authorization vs. Capture Intent

PayPal supports both one-phase and two-phase commit patterns, controlled by the `intent` field at order creation.

```json
// One-phase: Immediate capture on approval
{ "intent": "CAPTURE" }

// Two-phase: Hold funds on approval, capture later
{ "intent": "AUTHORIZE" }
```

**Authorization Hold Window (Critical Difference from Stripe — Three Distinct Paths):**

| Payment Source | Authorization Type | Validity |
|---|---|---|
| PayPal balance | Hold (funds reserved) | Up to **29 days** |
| Credit/debit card (card-funded PayPal) | Hold (funds reserved) | Up to **3 days** |
| eCheck / ACH (US-only) | **NOT a hold** — async settlement | 3–5 business days to clear |

> ⚠️ **eCheck is fundamentally different:** It is not an authorization hold — the funds are not reserved, they are in transit from the buyer's bank. PayPal sends `PAYMENT.CAPTURE.PENDING` (not `COMPLETED`) until settlement clears. This creates an unresolved Order saga state gap — see [Known Limitations and Phase 2 Scope](#known-limitations-and-phase-2-scope).

> **Authorization Expiry:** Use PayPal's returned `expiration_time` value for `PaymentAuthorized.ExpiresAt`. Do not compute expiry windows independently — PayPal knows the payment source type and sets the correct window. Note: `ExpiresAt` only applies to `AuthorizeAsync` flows; it is not relevant for one-phase `CaptureAsync` flows.

---

## The PayPal Checkout Flow: A Fundamental Difference from Stripe

This is the most architecturally significant difference to document.

### Stripe's Flow (Current Model)

```
Client (Storefront.Web)         Server (Payments.Api)           Stripe
        |                               |                           |
        |                               |-- POST /payment_intents-->|
        |                               |<-- client_secret ---------|
        |<--- return client_secret -----|                           |
        |                               |                           |
        |-- submit card (Stripe.js) ------------------------------ >|
        |<-- confirmation (Stripe.js) ----------------------------  |
        |                               |                           |
        |-- notify server via message ->|                           |
        |                               |-- Webhook (async) ------->|
```

**Key characteristic:** Card data NEVER touches our server. The client tokenizes, Stripe returns a PaymentMethod token, and our server uses that token in subsequent API calls. The `paymentMethodToken` in our `PaymentRequested` message is a Stripe `pm_xxx` token.

### PayPal's Standard Checkout Flow

```
Client (Storefront.Web)         Server (Payments.Api)           PayPal
        |                               |                           |
        |-- Request checkout ---------->|                           |
        |                               |-- POST /v2/checkout/orders|
        |                               |<-- orderID: "5O190127..." |
        |<--- return orderID ------------|                           |
        |                               |                           |
        |-- PayPal JS SDK renders "Pay with PayPal" button          |
        |-- Customer clicks button --------------------------------->|
        |<-- PayPal approval overlay (PayPal's domain) ------------ |
        |<-- Customer approves or cancels on PayPal's UI --------- |
        |                               |                           |
        | (JS SDK: onApprove callback with orderID)                 |
        |                               |                           |
        |-- send approved orderID ----->|                           |
        |                               |-- POST /v2/checkout/orders/{orderID}/capture
        |                               |<-- capture result --------|
        |                               |                           |
        |                               |==> PaymentCaptured event  |
        |                               |                           |
        |                        (webhook async)                    |
        |                               |<-- PAYMENT.CAPTURE.COMPLETED webhook
```

**The critical architectural implication:** The `paymentMethodToken` in our `PaymentRequested` message maps to the PayPal `orderID` returned after buyer approval — NOT a static saved token. However, before the buyer can approve, the SERVER must have created the order first (Step 2 above). This means:

1. A PayPal-specific endpoint `/api/paypal/orders` must exist on our server to create the order and return the `orderID` to the client before the buyer redirect.
2. The `IPaymentGateway.CaptureAsync(amount, currency, paymentMethodToken)` call maps cleanly to PayPal's capture step — the `paymentMethodToken` IS the `orderID`.
3. BUT the `IPaymentGateway` interface currently has NO method for step 1 (creating the order).

### IPaymentGateway Fit Analysis

| Operation | Stripe | PayPal | Gap? |
|---|---|---|---|
| Create Payment Intent / Order | ❌ (not needed — client tokenizes directly) | ✅ Required before buyer redirect | **Gap: new `CreateOrderAsync()`** |
| Capture one-phase | `CaptureAsync(amount, currency, token)` | Capture approved order: maps cleanly | ✅ fits |
| Authorize two-phase | `AuthorizeAsync(amount, currency, token)` | `intent: AUTHORIZE` + capture later | ✅ fits (with order creation caveat) |
| Capture authorized | `CaptureAuthorizedAsync(authId, amount)` | POST `/v2/payments/authorizations/{authId}/capture` | ✅ fits |
| Refund | `RefundAsync(txnId, amount)` | POST `/v2/payments/captures/{captureId}/refund` | ✅ fits |
| Partial refund | `RefundAsync(txnId, partialAmount)` | Supported (specify `amount` in refund body) | ✅ fits |
| Partial capture | Not in current interface | Supported (specify `amount_to_capture`) | ✅ would fit if interface added it |

**Recommendation:** Extend `IPaymentGateway` with `CreateOrderAsync()` specifically for the PayPal pre-redirect step. Alternatively, create a PayPal-specific `IPayPalGateway` interface that extends `IPaymentGateway`, keeping the base interface unchanged for Stripe compatibility.

```csharp
// Option A: Extend the shared interface
public interface IPaymentGateway
{
    // Existing methods...
    
    /// <summary>
    /// Creates a payment order server-side and returns an order reference for client-side approval.
    /// Used by gateways (PayPal) that require a server-created order before buyer redirect.
    /// Returns null / empty string for gateways (Stripe) that don't use this pattern.
    /// </summary>
    Task<GatewayResult> CreateOrderAsync(
        decimal amount,
        string currency,
        string intent,     // "CAPTURE" or "AUTHORIZE"
        CancellationToken ct);
}

// Option B: Gateway-specific sub-interface (preferred for clean separation)
public interface IPayPalGateway : IPaymentGateway
{
    Task<string> CreateOrderAsync(decimal amount, string currency, string intent, CancellationToken ct);
}
```

> ⚠️ **Open Question for Principal Architect:** Should `IPaymentGateway` be extended with `CreateOrderAsync`, or should this be handled as a PayPal-specific setup endpoint outside the gateway abstraction? The current abstraction is elegantly narrow; adding a gateway-specific step might compromise that. A separate PayPal bootstrap controller that creates the order and returns `orderID` to the client — then flows through `CaptureAsync` from that point — would keep the interface clean.

---

## PayPal API Operations: Request/Response Reference

### Authentication: Get Access Token

```http
POST https://api-m.sandbox.paypal.com/v1/oauth2/token
Authorization: Basic {Base64("client_id:client_secret")}
Content-Type: application/x-www-form-urlencoded

grant_type=client_credentials
```

```json
{
  "access_token": "A21AAFEpH4PsADK7qSS7pSRsgzfENtu-Q1ysgEDVDESseMHBYXVJYE8ovjj68elIDy8nF26AwPhfXTIeWAZHSLIsQkSYz9ifg",
  "token_type": "Bearer",
  "app_id": "APP-80W284485P519543T",
  "expires_in": 31668
}
```

### Create Order (Pre-Buyer Approval)

```http
POST https://api-m.sandbox.paypal.com/v2/checkout/orders
Authorization: Bearer {access_token}
Content-Type: application/json
PayPal-Request-Id: {idempotency-key}   // ← PayPal's idempotency mechanism

{
  "intent": "CAPTURE",
  "purchase_units": [
    {
      "reference_id": "critter-supply-order-{orderId}",
      "amount": {
        "currency_code": "USD",
        "value": "19.99"
      }
    }
  ]
}
```

```json
// Response: Order created, awaiting buyer approval
{
  "id": "5O190127TN364715T",
  "status": "CREATED",
  "links": [
    { "href": "https://api-m.sandbox.paypal.com/v2/checkout/orders/5O190127TN364715T", "rel": "self" },
    { "href": "https://www.sandbox.paypal.com/checkoutnow?token=5O190127TN364715T", "rel": "approve" },
    { "href": "https://api-m.sandbox.paypal.com/v2/checkout/orders/5O190127TN364715T/capture", "rel": "capture" }
  ]
}
```

> **Note on `reference_id`:** This is PayPal's mechanism for correlating a PayPal Order to your internal order. Populate this with your CritterSupply `orderId` (as a string). This field appears in webhooks, enabling correlation in webhook handlers without metadata lookup.

### Client-Side: PayPal JavaScript SDK

After the server creates the order and returns `orderID`, the client-side PayPal JS SDK renders the approval button:

```html
<!-- Include PayPal JS SDK -->
<script src="https://www.paypal.com/sdk/js?client-id={YOUR_CLIENT_ID}&currency=USD"></script>

<div id="paypal-button-container"></div>

<script>
  paypal.Buttons({
    // Server creates order, returns orderID
    createOrder: async function(data, actions) {
      const response = await fetch('/api/paypal/orders', { method: 'POST' });
      const order = await response.json();
      return order.id; // PayPal orderID
    },
    
    // Buyer approved → our server captures
    onApprove: async function(data, actions) {
      const response = await fetch(`/api/paypal/orders/${data.orderID}/capture`, {
        method: 'POST'
      });
      const capture = await response.json();
      // Show success to user
    },
    
    // Buyer cancelled
    onCancel: function(data) {
      // Handle cancellation (show cart, allow retry)
    },
    
    // Error occurred
    onError: function(err) {
      // Handle error
    }
  }).render('#paypal-button-container');
</script>
```

> ⚠️ **UX Implication:** The "Pay with PayPal" button is PayPal-branded and styled. Merchants have limited control over its appearance. This is a deliberate design decision by PayPal to maintain brand consistency and buyer trust. Storefront.Web must accommodate the PayPal button alongside any custom card payment UI.

### Capture Order (After Buyer Approval)

```http
POST https://api-m.sandbox.paypal.com/v2/checkout/orders/{orderID}/capture
Authorization: Bearer {access_token}
Content-Type: application/json
PayPal-Request-Id: {idempotency-key}

{}   // Empty body for standard full capture
```

```json
// Successful capture response
{
  "id": "5O190127TN364715T",
  "status": "COMPLETED",
  "purchase_units": [
    {
      "reference_id": "critter-supply-order-{orderId}",
      "payments": {
        "captures": [
          {
            "id": "3C679366HH908993F",        // ← This is our TransactionId
            "status": "COMPLETED",
            "amount": { "currency_code": "USD", "value": "19.99" },
            "seller_protection": {
              "status": "ELIGIBLE",
              "dispute_categories": ["ITEM_NOT_RECEIVED", "UNAUTHORIZED_TRANSACTION"]
            },
            "create_time": "2023-01-01T00:00:00Z",
            "update_time": "2023-01-01T00:00:00Z"
          }
        ]
      }
    }
  ],
  "payer": {
    "email_address": "customer@example.com",
    "payer_id": "QYR5Z8XDVJNXQ",
    "name": { "given_name": "John", "surname": "Doe" }
  }
}
```

> The capture `id` (`3C679366HH908993F`) becomes our `TransactionId` in `GatewayResult`. This maps cleanly to `PaymentCaptured.TransactionId`.

### Authorize Payment (Two-Phase, Step 1)

```http
POST https://api-m.sandbox.paypal.com/v2/checkout/orders
Authorization: Bearer {access_token}
PayPal-Request-Id: {idempotency-key}
Content-Type: application/json

{
  "intent": "AUTHORIZE",   // ← Key difference: hold funds, don't capture
  "purchase_units": [
    {
      "amount": { "currency_code": "USD", "value": "19.99" }
    }
  ]
}
```

After buyer approval, call:

```http
POST https://api-m.sandbox.paypal.com/v2/checkout/orders/{orderID}/authorize
Authorization: Bearer {access_token}
```

```json
// Authorization response
{
  "id": "5O190127TN364715T",
  "status": "COMPLETED",
  "purchase_units": [
    {
      "payments": {
        "authorizations": [
          {
            "id": "4X960614LD8786041",    // ← AuthorizationId for later capture
            "status": "CREATED",
            "amount": { "currency_code": "USD", "value": "19.99" },
            "expiration_time": "2023-01-04T00:00:00Z"   // ← PayPal-provided expiry
          }
        ]
      }
    }
  ]
}
```

> **Authorization Expiry:** `expiration_time` is set by PayPal (not us). Use this value for `PaymentAuthorized.ExpiresAt` rather than computing it ourselves. Card-funded authorizations expire in ~3 days; PayPal balance authorizations in up to 29 days.

### Capture Authorized Payment (Two-Phase, Step 2)

```http
POST https://api-m.sandbox.paypal.com/v2/payments/authorizations/{authorizationId}/capture
Authorization: Bearer {access_token}
PayPal-Request-Id: {idempotency-key}
Content-Type: application/json

{}   // Full capture; or specify amount for partial capture:
// { "amount": { "currency_code": "USD", "value": "10.00" } }
```

### Void/Cancel Authorization

```http
POST https://api-m.sandbox.paypal.com/v2/payments/authorizations/{authorizationId}/void
Authorization: Bearer {access_token}
```

Maps to our `CancelAuthorization` message from the Order saga (e.g., inventory reservation failed).

### Refund Captured Payment

```http
POST https://api-m.sandbox.paypal.com/v2/payments/captures/{captureId}/refund
Authorization: Bearer {access_token}
PayPal-Request-Id: {idempotency-key}
Content-Type: application/json

{}   // Full refund; or partial:
// { "amount": { "currency_code": "USD", "value": "9.99" } }
```

```json
// Refund response
{
  "id": "1JU08902781691411",
  "status": "COMPLETED",
  "amount": { "currency_code": "USD", "value": "19.99" }
}
```

---

## PayPal Webhook Events

PayPal delivers webhooks via HTTPS POST to your registered endpoint. Retry policy: **up to 25 attempts over 3 days** (similar to Stripe's 3-day retry window).

### Key Events for CritterSupply Payments BC

| PayPal Event | Stripe Equivalent | Our Response |
|---|---|---|
| `PAYMENT.CAPTURE.COMPLETED` | `payment_intent.succeeded` | Publish `PaymentCaptured` integration message |
| `PAYMENT.CAPTURE.DECLINED` | `payment_intent.payment_failed` | Publish `PaymentFailed` integration message |
| `PAYMENT.CAPTURE.PENDING` | (no direct equivalent) | Log, monitor — may transition to COMPLETED or DECLINED |
| `PAYMENT.CAPTURE.REFUNDED` | `charge.refunded` | Publish `RefundCompleted` integration message |
| `PAYMENT.REFUND.FAILED` | (no direct equivalent in Stripe webhooks) | Publish `RefundFailed` integration message |
| `PAYMENT.AUTHORIZATION.CREATED` | (no direct Stripe equivalent — Stripe uses API response) | Internal: confirm authorization in event stream |
| `PAYMENT.AUTHORIZATION.VOIDED` | (no direct Stripe equivalent) | Internal: handle authorization cancellation |
| `CHECKOUT.ORDER.APPROVED` | (no direct Stripe equivalent) | Trigger capture (in redirect-back flow) |
| `CUSTOMER.DISPUTE.CREATED` | `charge.dispute.created` | Alert, manual review path |

### Sample Webhook Payload

```json
{
  "id": "WH-2WR32451HC0233532-67976317FL4543714",
  "event_version": "1.0",
  "create_time": "2023-01-01T00:00:00.000Z",
  "resource_type": "capture",
  "resource_version": "2.0",
  "event_type": "PAYMENT.CAPTURE.COMPLETED",
  "summary": "Payment completed for $ 19.99 USD",
  "resource": {
    "id": "3C679366HH908993F",
    "status": "COMPLETED",
    "amount": {
      "currency_code": "USD",
      "value": "19.99"
    },
    "supplementary_data": {
      "related_ids": {
        "order_id": "5O190127TN364715T"
      }
    },
    "create_time": "2023-01-01T00:00:00Z",
    "update_time": "2023-01-01T00:00:00Z"
  },
  "links": [
    { "href": "https://api.paypal.com/v1/notifications/webhooks-events/WH-2WR32451HC0233532-67976317FL4543714", "rel": "self", "method": "GET" },
    { "href": "https://api.paypal.com/v1/notifications/webhooks-events/WH-2WR32451HC0233532-67976317FL4543714/resend", "rel": "resend", "method": "POST" }
  ]
}
```

> **Correlation:** Use `resource.supplementary_data.related_ids.order_id` to correlate the webhook to your CritterSupply order, and `resource.id` as the capture ID (our `TransactionId`).

---

## Webhook Security: Signature Verification

> ⚠️ **This is significantly more complex than Stripe.** See `docs/examples/paypal/WEBHOOK-SECURITY.md` for the full deep-dive.

PayPal uses **asymmetric RSA-SHA256 certificate-based verification**, NOT the simple HMAC-SHA256 that Stripe uses.

### Required HTTP Headers from PayPal

| Header | Description |
|---|---|
| `paypal-transmission-id` | Unique ID for this webhook delivery |
| `paypal-transmission-time` | ISO 8601 timestamp when message was sent |
| `paypal-cert-url` | URL of PayPal's X.509 certificate (download and cache) |
| `paypal-transmission-sig` | Base64-encoded RSA-SHA256 signature |
| `paypal-auth-algo` | Algorithm (e.g., `SHA256withRSA`) |

### Verification Algorithm

```
Signed message = "{transmissionId}|{timeStamp}|{webhookId}|{CRC32(rawBody)}"
```

Verify: `paypal-transmission-sig` (Base64 RSA signature) against `Signed message` using the public key from the certificate at `paypal-cert-url`.

> **CRC32 Note:** Unlike Stripe which hashes the body with HMAC-SHA256, PayPal uses CRC32 (a simpler checksum). The CRC32 value is expressed in decimal form in the signed message string.

### C# Verification Sketch

```csharp
private async Task<bool> VerifyPayPalWebhookSignature(
    HttpRequest request,
    string rawBody,
    string webhookId)
{
    var transmissionId = request.Headers["paypal-transmission-id"].ToString();
    var transmissionTime = request.Headers["paypal-transmission-time"].ToString();
    var certUrl = request.Headers["paypal-cert-url"].ToString();
    var transmissionSig = request.Headers["paypal-transmission-sig"].ToString();

    // 1. Compute CRC32 of raw body (decimal form)
    var crc32 = ComputeCrc32Decimal(rawBody);

    // 2. Build the signed message
    var message = $"{transmissionId}|{transmissionTime}|{webhookId}|{crc32}";

    // 3. Download and cache PayPal's certificate
    var certPem = await DownloadAndCacheCertificate(certUrl);

    // 4. Verify RSA-SHA256 signature
    using var rsa = GetPublicKeyFromCertificate(certPem);
    var signatureBytes = Convert.FromBase64String(transmissionSig);
    var messageBytes = Encoding.UTF8.GetBytes(message);

    return rsa.VerifyData(messageBytes, signatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
}
```

### Alternative: PayPal's Verify-Signature API

Instead of computing the signature yourself, you can POST the webhook data back to PayPal for verification:

```http
POST https://api-m.sandbox.paypal.com/v1/notifications/verify-webhook-signature
Authorization: Bearer {access_token}
Content-Type: application/json

{
  "auth_algo": "SHA256withRSA",
  "cert_url": "{paypal-cert-url header value}",
  "transmission_id": "{paypal-transmission-id header value}",
  "transmission_sig": "{paypal-transmission-sig header value}",
  "transmission_time": "{paypal-transmission-time header value}",
  "webhook_id": "{your registered webhook ID}",
  "webhook_event": { /* the complete webhook event JSON */ }
}
```

Response: `{ "verification_status": "SUCCESS" }` or `{ "verification_status": "FAILURE" }`

> **Trade-off:** The API-based verification is simpler to implement but adds latency and an external API dependency to every webhook handler invocation. Self-verification is preferred for production; the API approach is acceptable for initial implementation.

---

## PayPal Vault: Saved Payment Methods for Returning Customers

PayPal's **Vault** allows merchants to save customer payment methods for future purchases — directly analogous to Stripe's `PaymentMethod` + `Customer` pairing.

### What Can Be Vaulted

- PayPal account references (so customer doesn't need to redirect to PayPal again)
- Credit/debit card details (tokenized, PCI-compliant)

### Vault Flow for Returning Customers

```
First Purchase:
  Customer approves payment → Server creates vault reference
  PayPal returns: vault.id = "b8e2f4a8-c5d3-..."

Future Purchase (No Redirect Required!):
  Server uses vault.id as paymentMethodToken
  POST /v2/checkout/orders with payment_source.card.vault_id (or payment_source.paypal.vault_id)
  → Charges saved method WITHOUT buyer redirect ✅
```

### Vault Token in Order Payload

```json
// Creating order with a vaulted card (no buyer redirect needed)
{
  "intent": "CAPTURE",
  "payment_source": {
    "card": {
      "vault_id": "b8e2f4a8-c5d3-...",   // Vault token (maps to our paymentMethodToken)
      "stored_credential": {
        "payment_initiator": "MERCHANT",
        "payment_type": "UNSCHEDULED",
        "usage": "SUBSEQUENT"
      }
    }
  },
  "purchase_units": [{ "amount": { "currency_code": "USD", "value": "19.99" } }]
}
```

> **IPaymentGateway Compatibility:** When using a vault token, `paymentMethodToken` = vault ID. The server can call `CaptureAsync(amount, currency, vaultId)` directly without the buyer-redirect flow. This means our `IPaymentGateway.CaptureAsync()` IS compatible for returning customers — the vault flow is the "clean" path that maps to our existing interface.

---

## PayPal's Idempotency Mechanism

PayPal uses the `PayPal-Request-Id` header (equivalent to Stripe's `Idempotency-Key`):

```http
POST /v2/checkout/orders
PayPal-Request-Id: "create-order-{paymentId}"   // Deterministic, unique per operation
```

**Key Rules:**
- Must be unique per request type (use different prefixes for create vs capture vs refund)
- Scope is per REST application (not global)
- PayPal caches the response for a period (exact duration varies by endpoint)
- Recommended format: UUID or deterministic composite key

**Suggested Idempotency Keys:**
- Create order: `"paypal-create-{paymentId}"`
- Capture: `"paypal-capture-{paymentId}"`
- Authorize: `"paypal-auth-{paymentId}"`
- Void: `"paypal-void-{authorizationId}"`
- Refund: `"paypal-refund-{transactionId}-{amount}"`

---

## PayPal-Specific Business Features

### Pay Later (Buy Now, Pay Later)

PayPal's **Pay in 4** and **Pay Monthly** options are surfaced automatically via the JavaScript SDK — no additional API integration required from the merchant's server side.

```
Pay in 4: 4 equal interest-free payments over 6 weeks
Pay Monthly: Monthly installments for larger amounts
```

**Merchant perspective:** When a customer selects Pay Later, the capture on our end still completes as a standard `PAYMENT.CAPTURE.COMPLETED` webhook. PayPal manages the installment relationship with the buyer directly. Our `PaymentCaptured` event and `TransactionId` are populated normally.

**UX implication:** The PayPal Smart Button automatically shows Pay Later messaging (e.g., "Pay in 4 installments of $5.00") below the button when the amount is eligible. This can be a meaningful conversion driver for mid-to-high ticket pet supply orders (premium food bags, vet supply, grooming equipment).

### PayPal Buyer Protection

PayPal's Buyer Protection automatically covers eligible purchases against:
- Items not received
- Items significantly not as described

**Merchant implication:** This is a **buyer-facing feature** — it doesn't change our API integration. However, it IS a customer trust signal worth surfacing in the Storefront UI (a "Covered by PayPal Buyer Protection" badge). Our event model does NOT need to change; this is handled by PayPal's checkout receipt to the customer.

### PayPal Seller Protection

Protects merchants against unauthorized transactions and item-not-received disputes. Eligibility requires:
- Providing buyer's shipping address in the order
- Providing shipment tracking information via PayPal's API (optional but recommended)

**Implication for CritterSupply:** Achieving Seller Protection requires the Payments BC to receive (and pass to PayPal) the shipping address from the Shopping/Orders BC — currently not in `PaymentRequested`. This is a **potential interface extension** for a future cycle. For the initial PayPal implementation, this can be omitted (Seller Protection not guaranteed) and added in a subsequent hardening phase.

```http
// Optional: Add tracking info for Seller Protection
POST https://api-m.paypal.com/v1/shipping/trackers
{
  "transaction_id": "{captureId}",
  "tracking_number": "{shippingTrackingNumber}",
  "status": "SHIPPED",
  "carrier": "UPS"
}
```

### International Coverage and Currency

PayPal operates in **200+ countries** and supports **25+ currencies**. Currency handling:

- Merchants specify `currency_code` in purchase units
- PayPal handles currency conversion at the buyer's end (buyer pays in their local currency; merchant receives in their configured settlement currency)
- CritterSupply currently uses `"USD"` for all payments — no change required for initial integration

**Future consideration:** For international reference architecture scenarios, PayPal is the stronger choice over Stripe (which has more limited international support in some regions).

---

## Developer Experience Assessment

| Criterion | Stripe | PayPal | Notes |
|---|---|---|---|
| API design | ✅ REST + Objects (PaymentIntent) | ✅ REST + Objects (Orders API v2) | Comparable |
| Test/sandbox mode | ✅ Excellent | ✅ Robust | Both provide sandbox environments |
| CLI tooling | ✅ `stripe listen` for local webhooks | ⚠️ No CLI equivalent | Use ngrok or PayPal Sandbox Simulator |
| Documentation quality | ✅ Industry-leading | ⚠️ Multiple doc sites, some sections outdated | Orders API v2 docs are good; older sections vary |
| SDK approach | ✅ Official Stripe .NET SDK | ✅ Direct HTTP preferred | Direct `HttpClient` to Orders API v2 is the right approach — no SDK version lag |
| Webhook security | ✅ HMAC-SHA256 (20–30 lines) | ⚠️ RSA-SHA256 + CRC32 (40–60 lines + cert caching) | PayPal requires more implementation effort |
| Two-phase commit | ✅ Native (`capture_method: manual`) | ✅ `intent: AUTHORIZE` | Both support it |
| Idempotency | ✅ `Idempotency-Key` header | ✅ `PayPal-Request-Id` header | Equivalent |
| Sandbox test accounts | ✅ Static test card numbers | ⚠️ Browser-based buyer account login | PayPal sandbox requires creating accounts and interactive approval |
| Auth token model | ✅ Static API key | ⚠️ OAuth tokens (expire every ~8 hrs) | PayPal requires token refresh logic — implement this first |
| Webhook simulator | ✅ `stripe trigger` (CLI) | ✅ Dashboard Webhooks Simulator | PayPal Simulator is good for handler testing without full buyer flow |

### SDK Note: Prefer Direct HTTP

PayPal offers a `PayPalCheckoutSdk` NuGet package, but the preferred approach for Orders API v2 integration is direct `HttpClient` calls (as shown in the example code). This avoids SDK version lag, gives full control over request/response shapes, and follows the same pattern used in CritterSupply's external service integration pattern.

### Local Webhook Testing

Since PayPal has no CLI equivalent to `stripe listen`, two options exist:

**Option A: ngrok (for full end-to-end testing)**
```bash
ngrok http 5232
# Yields: https://abc123.ngrok.io → http://localhost:5232
# Register this URL in PayPal Developer Dashboard → Your App → Webhooks
```

**Option B: PayPal Sandbox Webhooks Simulator (for handler-only testing)**
- Go to: [developer.paypal.com/dashboard/webhooks](https://developer.paypal.com/dashboard/webhooks)
- Select "Simulate Webhook Event"
- Choose event type (e.g., `PAYMENT.CAPTURE.COMPLETED`)
- Simulator posts to your registered endpoint
- **Does not require buyer approval flow** — ideal for testing webhook handler logic in isolation
- Note: Simulator webhooks use `"WEBHOOK_ID"` as the webhook ID for postback verification; self-verification (local RSA method) works normally

**Recommendation:** Use the Sandbox Simulator for unit/handler testing; use ngrok only when you need full end-to-end buyer approval flow testing.

---

## Key Gotchas and Pitfalls

### 1. Access Token Expiry (Most Common Integration Bug)

**Problem:** PayPal access tokens expire in ~8 hours. If you cache the token at startup and never refresh it, API calls will fail with `401 Unauthorized` after expiry.

**Solution:** Implement token caching with TTL-aware refresh:

```csharp
private sealed class CachedToken
{
    public string AccessToken { get; init; } = default!;
    public DateTimeOffset ExpiresAt { get; init; }
    public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt.AddSeconds(-60); // 60s buffer
}
```

### 2. Order ID vs. Capture ID vs. Authorization ID Confusion

PayPal has three different ID types that all look like strings:
- **Order ID** (`5O190127TN364715T`) — the top-level container, created before buyer approval
- **Capture ID** (`3C679366HH908993F`) — the actual charge, our `TransactionId`
- **Authorization ID** (`4X960614LD8786041`) — funds hold reference, our `authorizationId`

**Gotcha:** Webhooks reference `resource.id` = Capture ID. For refunds, you need the Capture ID, NOT the Order ID.

### 3. Webhook `webhookId` Must Match Registration

PayPal's signature verification requires your registered `webhookId` (from the dashboard or API). Unlike Stripe's `whsec_xxx` secret key approach, PayPal uses the `webhookId` (a different concept). Mixing these up is a common source of verification failures.

### 4. Certificate Caching Required

PayPal's self-verification method requires downloading the signing certificate from `paypal-cert-url`. Failing to cache this certificate results in an external HTTP call on every webhook — serious performance and reliability concern. Cache the certificate with a TTL of several hours.

### 5. The "PENDING" State

Some PayPal captures land in `PENDING` status before completing. This happens for eCheck payments, certain international transactions, and fraud review holds. Your webhook handler must handle `PAYMENT.CAPTURE.PENDING` gracefully — do NOT assume the payment is complete until `PAYMENT.CAPTURE.COMPLETED` is received.

### 6. Currency Amounts as Strings

PayPal represents monetary amounts as **decimal strings** (`"19.99"`), NOT integers in cents like Stripe (`1999`). Do NOT need to convert to cents for PayPal; DO NOT pass integers. Parse the string to `decimal` in .NET.

```csharp
// Stripe: amount in cents (integer)
{ "amount": 1999, "currency": "usd" }

// PayPal: amount as decimal string
{ "amount": { "currency_code": "USD", "value": "19.99" } }
```

### 7. No Test Card Numbers — Use Sandbox Accounts

PayPal sandbox does not use magic card numbers like Stripe (`4242 4242 4242 4242`). Instead, you log into the sandbox buyer account at `sandbox.paypal.com` to simulate buyer approval. This requires a browser / WebDriver for automated testing of the approval step.

### 8. Pay Later Messaging Requires Minimum SDK Configuration

To show Pay Later messaging, include the `enable-funding=paylater` parameter in the JS SDK URL:

```html
<script src="https://www.paypal.com/sdk/js?client-id={CLIENT_ID}&enable-funding=paylater&currency=USD"></script>
```

---

## Security Considerations

### 1. Client ID vs. Client Secret

| Secret | Where Used | Exposure Level |
|---|---|---|
| Client ID | Client-side JavaScript SDK (public) | ✅ Safe to expose |
| Client Secret | Server-side only, NEVER client-side | 🔐 Must protect |
| Access Token | Server-side API calls | 🔐 Must protect (short-lived) |
| Webhook ID | Server-side verification | ⚠️ Keep private — needed for signature verification |

**Development (User Secrets):**
```bash
dotnet user-secrets set "PayPal:ClientId" "your-sandbox-client-id"
dotnet user-secrets set "PayPal:ClientSecret" "your-sandbox-client-secret"
dotnet user-secrets set "PayPal:WebhookId" "your-sandbox-webhook-id"
```

**Production:** Environment variables or secret management (Azure Key Vault, AWS Secrets Manager).

### 2. Webhook Endpoint Security

- Always validate PayPal webhook signatures (see [Webhook Security](#webhook-security-signature-verification))
- Return `200 OK` for all valid webhooks (including duplicates) to prevent retries
- Implement event deduplication: store processed `webhook.id` values in Marten
- Certificate cache: store downloaded certs with 24-hour TTL

### 3. PCI Compliance

PayPal's redirect-based checkout (buyer approves on PayPal's site) significantly reduces PCI scope:
- Card data never touches CritterSupply's servers
- PCI SAQ A (simplest level) may be applicable for PayPal-only flows
- For card-on-file flows (Vault), PCI SAQ A-EP applies

### 4. HTTPS Required

PayPal only delivers webhooks to HTTPS endpoints (port 443). Local development requires a tunneling solution (ngrok, Cloudflare Tunnel).

---

## IPaymentGateway Extension Recommendations

**Decision (Product Owner, 2026-03-13):** Keep `IPaymentGateway` unchanged. PayPal's order-creation step belongs in a dedicated PayPal controller outside the gateway abstraction.

### Chosen Architecture: Dedicated PayPal Controller (Option 1)

```
[Storefront.Web] → POST /api/paypal/orders → [Payments.Api/PayPal/CreatePayPalOrder.cs]
                                               ↓
                                           PayPal: POST /v2/checkout/orders
                                               ↓
                                           returns orderID to client
                                               
[Storefront.Web] → PayPal JS SDK (buyer approves)
                                               
[Storefront.Web] → POST /api/paypal/orders/{id}/capture → [Payments.Api/PayPal/CapturePayPalOrder.cs]
                                               ↓
                                           IPaymentGateway.CaptureAsync(amount, currency, orderID)
```

**Rationale:**
- `IPaymentGateway`'s value as a teaching artifact depends on it being a clean, provider-agnostic contract. Adding a method that is meaningful for PayPal but a no-op for Stripe undermines that lesson.
- Creating a PayPal order is not the same domain concept as authorizing or capturing a payment — it's a PayPal-specific bootstrapping step. Domain concept alignment matters for a reference architecture.
- The vault flow (returning customers with saved PayPal accounts) maps cleanly to `CaptureAsync(amount, currency, vaultId)` — no interface extension needed for that path.

**Rejected Alternative:** Extended interface with `CreatePaymentSessionAsync()`. While technically possible, this would pollute the abstraction with provider-specific concerns and confuse developers learning from the codebase.

---

## Abandoned Order Cleanup

When a customer clicks "Pay with PayPal," a server-side order is created in PayPal's system. If the customer then abandons — closes the overlay, navigates away, or cancels — that PayPal order becomes a dangling object.

**What PayPal does:**
- A `CREATED` order with no buyer approval auto-expires after approximately 3 hours (PayPal does not provide a webhook for this expiry)
- An `APPROVED` but not-yet-captured order expires in a shorter window (minutes)
- A voided order (if the merchant explicitly voids it) triggers `PAYMENT.AUTHORIZATION.VOIDED`

**What we must handle:**

| Abandonment Scenario | PayPal Order State | Our Action |
|---|---|---|
| Customer closes overlay immediately | `CREATED` (no approval) | Create new order on next attempt (idempotency key change) |
| Customer cancels in PayPal UI | `CREATED` (PayPal `onCancel` fires) | JS SDK `onCancel` callback — restore checkout UI |
| Buyer approved, our capture fails | `APPROVED` | Retry capture (idempotency key prevents duplicate charge) |
| Inventory fails after authorization | `AUTHORIZED` | Void authorization via `POST /v2/payments/authorizations/{id}/void` |

**Saga State During Abandoned PayPal Flow:**

The Order saga remains in `PendingPayment` state during the entire buyer-approval window. If the customer abandons, no `PaymentFailed` message is published — the saga is simply never advanced. To prevent permanently stuck sagas, a timeout should be configured:

```
// Future implementation: Wolverine delayed message to handle abandoned orders
await outgoing.Delay(new PaymentApprovalExpired(orderId, paymentId), TimeSpan.FromMinutes(30));
```

This is not currently in scope for Phase 1 but should be documented as a known saga edge case.

**Recommended Production Checklist Item:** Add a `PayPalOrderExpiredJob` background service that queries PayPal for orders in `CREATED` state older than 3 hours and voids/logs them to prevent accumulation.

---

## Known Limitations and Phase 2 Scope

The following limitations apply to the initial (Phase 1) PayPal integration and are accepted by the Product Owner:

### 1. Seller Protection — Not Eligible in Phase 1

PayPal Seller Protection requires the buyer's shipping address to be present in the order. Our `PaymentRequested` message does not currently carry shipping address data.

**Impact:** Initial PayPal transactions will NOT automatically qualify for Seller Protection against item-not-received disputes.

**Phase 2:** Extend `PaymentRequested` with an optional `ShippingAddress` field. Populate this from the Order/Fulfillment BC when available and pass it to PayPal in the order creation step.

### 2. Dispute Handling — Not Implemented in Phase 1

When `CUSTOMER.DISPUTE.CREATED` arrives, the current handler logs a warning. No automated dispute response path exists.

**Impact:** Disputes require manual intervention via PayPal dashboard. PayPal auto-resolves in favor of the buyer if the merchant doesn't respond within their deadline.

**Phase 2:** Implement `PaymentDisputed` integration message flowing from Payments → Orders, triggering a manual review path in the Order saga. The `TransactionId` (capture ID) stored in `PaymentCaptured` is the exact data needed to respond to a dispute.

### 3. eCheck / ACH PENDING State — Saga Gap

When a buyer pays via eCheck (PayPal/ACH), PayPal sends `PAYMENT.CAPTURE.PENDING` instead of `PAYMENT.CAPTURE.COMPLETED`. This is NOT a temporary hold — it is async settlement that can take 3–5 business days.

**Current saga gap:** The Order saga has no `PaymentPending` state distinct from `PendingPayment`. If a `PAYMENT.CAPTURE.PENDING` event arrives:
- We cannot publish `PaymentCaptured` (funds not cleared)
- We cannot publish `PaymentFailed` (payment hasn't failed)
- The saga is stuck in `PendingPayment` indefinitely

**Phase 2:** Add `PaymentPending` saga state. Payments BC publishes `PaymentPending` integration message. Order saga waits in `PaymentPending` state until a subsequent `PAYMENT.CAPTURE.COMPLETED` or `PAYMENT.CAPTURE.DECLINED` webhook resolves the state.

> ⚠️ **Note:** eCheck is US-only (ACH-based). This gap does not affect international transactions. CritterSupply's US-focused reference architecture may defer this, but should NOT silently ignore `PAYMENT.CAPTURE.PENDING` events — they must be logged and the operator alerted.

### 4. Auto-ship / Recurring Payments — Not In Scope

PayPal Vault supports merchant-initiated transactions (MIT) for scheduled/unattended charges, which is the foundation for an auto-ship feature. Pet supply is a strong subscription commerce category (Chewy's Autoship, Amazon Subscribe & Save are major revenue drivers).

**Phase 3+:** Evaluate PayPal recurring billing alongside Stripe recurring payments if a Subscriptions BC is planned.

### 5. PayPal Identity Data Gap

When a customer pays with PayPal, PayPal returns the buyer's email, name, and PayPal account ID in the capture response. Currently, none of this flows to Customer Identity BC.

**Known gap:** Customer Identity BC has no path to learn a customer's PayPal account association from a successful payment. Future work may want to correlate CritterSupply customer IDs with PayPal payer IDs for improved customer service and fraud prevention.

### 6. PCI Scope in Mixed-Gateway Deployment

When PayPal and Stripe operate simultaneously:
- **PayPal redirect flow**: Keeps card data off CritterSupply servers → PCI SAQ A scope for PayPal transactions
- **Stripe Elements**: Keeps card data off CritterSupply servers → PCI SAQ A-EP scope for Stripe transactions
- **Combined**: The Stripe integration's PCI SAQ A-EP requirement is the binding constraint. Adding PayPal does NOT increase PCI scope — it is strictly additive. Developers should not assume PayPal's SAQ A rating applies to the entire system.

---

## Production Checklist

### Phase 1: Core Integration
- [ ] Store `PayPal:ClientId`, `PayPal:ClientSecret`, and `PayPal:WebhookId` in secret management (not `appsettings.json`)
- [ ] Implement access token caching with TTL-aware refresh (critical — tokens expire in ~8 hours)
- [ ] Implement PayPal webhook signature verification (RSA-SHA256 + CRC32)
- [ ] Validate `paypal-cert-url` domain before downloading (SSRF protection)
- [ ] Cache PayPal signing certificates (24-hour TTL minimum)
- [ ] Store processed webhook event IDs in Marten (deduplication — PayPal retries 25 times over 3 days)
- [ ] Handle `PAYMENT.CAPTURE.PENDING` state: log + alert, do NOT publish `PaymentCaptured` (saga gap — see Phase 2)
- [ ] Handle `onCancel` in PayPal JS SDK: restore checkout UI gracefully
- [ ] Use `PayPal-Request-Id` header for all mutating API calls (idempotency)
- [ ] Configure sandbox and production webhook endpoints separately
- [ ] Add structured logging: PayPal order IDs, capture IDs, event types, amounts, correlation IDs
- [ ] Monitor access token refresh failures (silently breaks all payment processing if missed)
- [ ] Set up alerts for `PAYMENT.CAPTURE.DECLINED` rate spikes
- [ ] Test Pay Later display in sandbox (requires `enable-funding=paylater` SDK parameter)
- [ ] Document rollback procedures: void authorization vs. refund after capture
- [ ] Document abandoned PayPal order lifecycle for operations team

### Phase 2: Hardening
- [ ] Extend `PaymentRequested` with optional `ShippingAddress` for Seller Protection eligibility
- [ ] Add `PaymentPending` integration message and Order saga state for eCheck support
- [ ] Implement `PaymentDisputed` integration message and Order saga compensation path
- [ ] Implement Vault support for returning customers (reduces checkout friction significantly)
- [ ] Implement abandoned PayPal order cleanup (background job or TTL-based)
- [ ] Add shipment tracking submission (`POST /v1/shipping/trackers`) for Seller Protection

---

## Open Questions for Architecture Review

The following questions require decisions before implementation can begin:

| Question | Status | Decision/Next Step |
|---|---|---|
| IPaymentGateway extension strategy | ✅ Closed | Option 1: external PayPal controller; interface unchanged |
| `PaymentAuthorized.ExpiresAt` source | ✅ Closed | Use PayPal's returned `expiration_time`; note: only applies to `AuthorizeAsync` flows, not `CaptureAsync` |
| Seller Protection data | ✅ Closed | Phase 2 scope; document as known limitation in Phase 1 |
| PayPal identity data in `PaymentCaptured` | ✅ Closed | Do NOT include; Customer Identity BC remains authoritative |
| Multi-gateway routing | 🚩 **Needs ADR** | Who decides Stripe vs. PayPal? What happens on retry? Escalate to Principal Architect |
| Local webhook testing | ✅ Closed | ngrok primary; PayPal Sandbox Simulator for handler-only testing |
| eCheck / `PAYMENT.CAPTURE.PENDING` saga state | ⚠️ Phase 2 | Add `PaymentPending` saga state; Phase 1 logs + alerts only |
| Abandoned order cleanup | ⚠️ Phase 2 | `PaymentApprovalExpired` delayed message; Phase 1 logs only |

> **Multi-gateway routing ADR:** The question of how the checkout flow routes between Stripe and PayPal affects Shopping BC (checkout wizard UI), Orders BC (payment command construction), and Payments BC (which gateway to use). This is a significant architectural decision that warrants a dedicated ADR. Working title: **"ADR XXXX: Multi-Gateway Payment Routing and Provider Selection."**

---

## References

### Internal Documents
- **Stripe Research Spike:** `docs/planning/spikes/stripe-api-integration.md`
- **Payment Gateway Comparison:** `docs/planning/spikes/payment-gateway-comparison.md`
- **ADR 0010 (Stripe):** `docs/decisions/0010-stripe-payment-gateway-integration.md`
- **IPaymentGateway Interface:** `src/Payments/Payments/Processing/IPaymentGateway.cs`
- **StubPaymentGateway:** `src/Payments/Payments/Processing/StubPaymentGateway.cs`
- **CONTEXTS.md (Payments BC):** `CONTEXTS.md` — Payments section
- **External Service Integration Pattern:** `docs/skills/external-service-integration.md`

### External Documentation
- [PayPal Orders API v2](https://developer.paypal.com/docs/api/orders/v2/) (current recommended API)
- [PayPal Payments API v2](https://developer.paypal.com/docs/api/payments/v2/) (authorizations, captures, refunds)
- [PayPal JS SDK Reference](https://developer.paypal.com/sdk/js/reference/)
- [PayPal Webhook Event Names](https://developer.paypal.com/api/rest/webhooks/event-names/)
- [PayPal Webhook Integration Guide](https://developer.paypal.com/api/rest/webhooks/rest/)
- [PayPal Vault (Payment Method Tokens)](https://developer.paypal.com/docs/multiparty/vault/)
- [PayPal Developer Dashboard (Sandbox)](https://developer.paypal.com/dashboard/)
- [PayPal REST API Authentication](https://developer.paypal.com/docs/api/overview/)
- [PayPal Pay Later Messaging](https://developer.paypal.com/docs/checkout/pay-later/us/integrate/messaging/)
- [PayPal Seller Protection](https://www.paypal.com/us/webapps/mpp/security/seller-protection)

---

## Appendix A: Product Owner Brief

The following is an excerpt from the Product Owner consultation conducted at the start of this research spike, included here as a permanent record of the business objectives that framed this research.

> **On strategic positioning:**
> "PayPal should be researched as a complementary option, not a replacement. Stripe serves developers and card-based payments — it's our technical reference implementation. PayPal serves customers who don't want to type their card number. In real pet supply e-commerce, PayPal checkout buttons are on most competitors' sites — Chewy, PetSmart, PetCo."

> **On the most important research question:**
> "Your most important research question: Does PayPal's buyer-redirect OAuth flow fit cleanly into our `paymentMethodToken: string` model, or does it require a two-step setup (create order → get approval → capture)? The answer determines whether `IPaymentGateway` needs an extension, or whether a PayPal-specific bootstrap endpoint outside the gateway abstraction handles the redirect handshake, with our existing interface handling the final capture."

> **On PayPal-specific features worth researching:**
> "Pay Later is relevant for larger pet supply purchases — premium food, vet supplies, large equipment. Seller Protection — what transaction data or shipping confirmation does a merchant need to provide? The PayPal identity data question: when a customer pays with PayPal, PayPal returns name, email, and shipping address. Should Customer Identity BC receive this as a verified data source, or does our own Customer Identity BC remain authoritative?"

> **On PCI compliance:**
> "PayPal's customer-redirect flow has a PCI compliance advantage: when customers pay via PayPal, they enter their credentials on PayPal's domain, not ours. This reduces our PCI scope. Developers reading this code will make real-world implementation decisions — getting PCI scope right is not optional."

---

## Product Owner Feedback

*Reviewed by Product Owner on 2026-03-13 after first draft was complete.*

### Overall Assessment

> "This is a strong research spike. The technical depth is appropriate, the Stripe comparison tables are genuinely useful for developers picking this up cold, and the UX Engineer clearly understood the assignment — this isn't just 'how does PayPal work' but 'how does PayPal fit into *our* system.' That's the right framing."

### Decision Table

| Open Question | PO Decision | Notes |
|---|---|---|
| IPaymentGateway extension | **Option 1 confirmed — interface unchanged** | External PayPal controller; don't pollute the abstraction |
| `PaymentAuthorized.ExpiresAt` | **Use PayPal's `expiration_time`** | More accurate; adapts to payment source type automatically |
| Seller Protection | **Phase 2 scope** | Known limitation for Phase 1; document in production checklist |
| PayPal identity in `PaymentCaptured` | **Do NOT include** | Customer Identity BC remains authoritative |
| Multi-gateway routing | **Needs dedicated ADR** | Affects Shopping, Orders, and Payments BCs — too consequential for a spike decision |
| Local webhook testing | **ngrok as primary** | Already documented; add Sandbox Simulator note for handler-only testing |

### PO-Requested Additions (Incorporated in This Document)

1. **Abandoned Order Cleanup section** — Added. The ~70% abandonment rate is a real operational concern; dangling PayPal orders, stuck saga states, and the `onCancel` callback handling needed explicit coverage.

2. **`PAYMENT.CAPTURE.PENDING` / eCheck saga gap** — Expanded. The Order saga has no `PaymentPending` state. eCheck is US-only (ACH-based) but cannot be silently ignored. Phase 2 resolution documented.

3. **eCheck is distinct from card authorization** — Clarified in authorization window table. eCheck is async settlement (not a hold) and should not be conflated with card auth/capture.

4. **Mobile checkout business case** — Added to executive summary. PayPal's one-tap approval from saved accounts is disproportionately valuable on mobile, where re-entering payment credentials is a major friction point for pet supply purchases.

5. **Phase 2 Known Limitations section** — Added. Documents Seller Protection, dispute handling, eCheck saga gap, auto-ship foundation, identity gap, and PCI mixed-gateway scope as accepted Phase 1 limitations with explicit Phase 2 resolution paths.

6. **PCI scope clarification for mixed-gateway deployment** — Added to Known Limitations. Stripe's SAQ A-EP is the binding constraint; PayPal does not increase scope.

### PO-Requested Corrections

1. **Authorization window framing** — Updated. eCheck is not the same as a card authorization hold; it's async settlement. Three distinct paths now documented with appropriate framing.

2. **Developer Experience section tone** — Softened. "inferior" framing was judgmental; replaced with neutral "requires more setup" framing throughout. The direct HTTP approach (bypassing the PayPal .NET SDK) is reframed as a positive choice for maintainability, not a workaround.

3. **Dispute lifecycle** — Added note in Known Limitations that `TransactionId` (capture ID) is the exact data needed to respond to `CUSTOMER.DISPUTE.CREATED`, and that Phase 2 requires a `PaymentDisputed` integration message.

### Items the PO Endorsed Strongly

- The recommendation to keep `IPaymentGateway` unchanged and use an external PayPal controller — *"preserves the abstraction's value as a teaching artifact."*
- Using direct HTTP calls to PayPal's Orders API v2 rather than the PayPal .NET SDK — *"more maintainable; no SDK version lag."*
- Vault support as a key business outcome: *"A returning customer with a vaulted PayPal account can complete checkout in two clicks — comparable to or better than Stripe's saved card experience."*
- Pay Later's business case for mid-ticket pet supply orders (premium food, grooming equipment).

### Remaining Open Item (Not In This Document)

The multi-gateway routing ADR (Question 5) must be written before implementation planning begins. This spike can serve as the research foundation for that ADR. Suggested ADR title: **"Multi-Gateway Payment Routing and Provider Selection."**
