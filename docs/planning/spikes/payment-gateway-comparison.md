# Payment Gateway API Comparison: Stripe, Braintree, Square

**Date:** 2026-02-18  
**Status:** Research Complete  
**Purpose:** Compare payment gateway APIs to identify common patterns and gateway-specific differences for designing a robust `IPaymentGateway` abstraction

## Executive Summary

This document compares three major payment gateway APIs—Stripe, Braintree (PayPal), and Square—to identify common patterns that should be abstracted in our `IPaymentGateway` interface vs. gateway-specific implementation details.

**Key Findings:**

✅ **Common Patterns Across All Three:**
- Two-phase commit (authorize → capture) support
- Webhook-based async notifications
- Idempotency mechanisms for safe retries
- Customer and payment method management
- Refund support with partial refund capabilities
- Similar error handling patterns (transient vs permanent failures)

⚠️ **Key Differences:**
- **API Design Philosophy:** Stripe (REST + objects), Braintree (GraphQL + SDK-first), Square (REST + simplified)
- **Authentication:** Stripe (bearer token), Braintree (public/private key + client token), Square (OAuth 2.0 + bearer)
- **Transaction Model:** Stripe (PaymentIntent → Charge), Braintree (Transaction), Square (Payment)
- **Webhook Security:** Different signature algorithms (HMAC-SHA256 vs. RSA vs. HMAC-SHA1)
- **Client Integration:** Stripe (client_secret), Braintree (payment_method_nonce), Square (source_id)

## Side-by-Side Comparison

### 1. Core Concepts

| Concept | Stripe | Braintree | Square |
|---------|--------|-----------|--------|
| **Primary Transaction Object** | `PaymentIntent` | `Transaction` | `Payment` |
| **Payment Method** | `PaymentMethod` (pm_xxx) | `PaymentMethodNonce` / `PaymentMethod` | `Card` / `SourceId` |
| **Customer** | `Customer` (cus_xxx) | `Customer` | `Customer` |
| **Authorization Hold** | `PaymentIntent` (status: requires_capture) | `Transaction` (submitted_for_settlement: false) | `Payment` (status: APPROVED, autocomplete: false) |
| **Completed Payment** | `Charge` (ch_xxx) linked to PaymentIntent | `Transaction` (status: settled) | `Payment` (status: COMPLETED) |
| **Refund** | `Refund` (re_xxx) | `Transaction` (type: refund) | `Refund` |

### 2. Authorization/Capture Flow Comparison

#### Stripe: PaymentIntent-Based Two-Phase Commit

```csharp
// AUTHORIZE (Step 1)
POST /v1/payment_intents
{
  "amount": 1999,
  "currency": "usd",
  "customer": "cus_xxx",
  "payment_method": "pm_xxx",
  "capture_method": "manual",  // Key: manual capture mode
  "confirm": true
}

// Response: status = "requires_capture"
{
  "id": "pi_xxx",
  "status": "requires_capture",
  "amount": 1999
}

// CAPTURE (Step 2)
POST /v1/payment_intents/pi_xxx/capture
{
  "amount_to_capture": 1999  // Optional: partial capture
}

// Response: status = "succeeded"
{
  "id": "pi_xxx",
  "status": "succeeded",
  "charges": {
    "data": [{"id": "ch_xxx", "captured": true}]
  }
}
```

**Key Characteristics:**
- Single object (`PaymentIntent`) through entire lifecycle
- Status transitions: `requires_payment_method` → `requires_capture` → `succeeded`
- Clean separation: authorize creates intent, capture settles it
- `Charge` object created automatically on successful capture

#### Braintree: Transaction-Based Two-Phase Commit

```csharp
// AUTHORIZE (Step 1)
gateway.Transaction.Sale(new TransactionRequest
{
    Amount = 19.99M,
    PaymentMethodNonce = "nonce_from_client",
    CustomerId = "customer_123",
    Options = new TransactionOptionsRequest
    {
        SubmitForSettlement = false  // Key: don't auto-settle
    }
});

// Response: status = AUTHORIZED
{
  "id": "txn_xxx",
  "status": "authorized",
  "amount": "19.99"
}

// CAPTURE (Step 2)
gateway.Transaction.SubmitForSettlement(
    "txn_xxx",
    19.99M  // Optional: partial capture
);

// Response: status = SUBMITTED_FOR_SETTLEMENT (then SETTLING → SETTLED)
{
  "id": "txn_xxx",
  "status": "submitted_for_settlement"
}
```

**Key Characteristics:**
- `Transaction` object from start (not separate intent vs charge)
- Status transitions: `authorized` → `submitted_for_settlement` → `settling` → `settled`
- Settlement is async (batched, often next business day)
- Single transaction ID throughout lifecycle

#### Square: Payment-Based Two-Phase Commit

```csharp
// AUTHORIZE (Step 1)
POST /v2/payments
{
  "source_id": "cnon_xxx",  // Card nonce from client
  "amount_money": {
    "amount": 1999,
    "currency": "USD"
  },
  "customer_id": "customer_xxx",
  "autocomplete": false,  // Key: manual capture
  "location_id": "location_xxx"
}

// Response: status = "APPROVED"
{
  "payment": {
    "id": "payment_xxx",
    "status": "APPROVED",
    "amount_money": {"amount": 1999, "currency": "USD"}
  }
}

// CAPTURE (Step 2)
POST /v2/payments/payment_xxx/complete
{
  "version_token": "vtoken_xxx"  // Optimistic concurrency control
}

// Response: status = "COMPLETED"
{
  "payment": {
    "id": "payment_xxx",
    "status": "COMPLETED"
  }
}
```

**Key Characteristics:**
- `Payment` object from start (like Braintree)
- Status transitions: `APPROVED` → `COMPLETED`
- Requires `location_id` (Square's merchant location concept)
- Version tokens for optimistic concurrency (prevents double-capture)

### 3. Webhook Patterns

All three gateways use webhooks for async notifications, but with different implementations:

#### Stripe Webhooks

**Event Types:**
- `payment_intent.created`
- `payment_intent.succeeded`
- `payment_intent.payment_failed`
- `charge.captured`
- `charge.refunded`

**Payload Structure:**
```json
{
  "id": "evt_xxx",
  "type": "payment_intent.succeeded",
  "data": {
    "object": {
      "id": "pi_xxx",
      "status": "succeeded",
      "metadata": {"order_id": "ord_123"}
    }
  }
}
```

**Security:**
- **Signature Header:** `Stripe-Signature`
- **Algorithm:** HMAC-SHA256
- **Signed Payload:** `timestamp.payload`
- **Verification:**
  ```csharp
  var signedPayload = $"{timestamp}.{payload}";
  using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(webhookSecret));
  var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(signedPayload));
  ```

#### Braintree Webhooks

**Event Types:**
- `subscription_charged_successfully`
- `subscription_charged_unsuccessfully`
- `transaction_settled`
- `transaction_settlement_declined`
- `disbursement`

**Payload Structure:**
```xml
<!-- Braintree sends XML (legacy) or can use webhook gateway for JSON -->
<notification>
  <kind>transaction_settled</kind>
  <subject>
    <transaction>
      <id>txn_xxx</id>
      <status>settled</status>
    </transaction>
  </subject>
</notification>
```

**Security:**
- **Verification via SDK:**
  ```csharp
  WebhookNotification notification = gateway.WebhookNotification.Parse(
      bt_signature: signature,
      bt_payload: payload
  );
  // SDK verifies signature using RSA public key
  ```
- **Algorithm:** RSA signature verification (not HMAC)
- **Challenge Parameter:** Braintree sends challenge string during webhook setup

**Note:** Braintree's webhook system is more complex due to PayPal integration—many events are subscription-focused. For simple transaction flows, polling `Transaction.Find()` may be simpler.

#### Square Webhooks

**Event Types:**
- `payment.created`
- `payment.updated`
- `refund.created`
- `refund.updated`

**Payload Structure:**
```json
{
  "merchant_id": "merchant_xxx",
  "type": "payment.updated",
  "event_id": "evt_xxx",
  "created_at": "2026-02-18T10:00:00Z",
  "data": {
    "type": "payment",
    "id": "payment_xxx",
    "object": {
      "payment": {
        "id": "payment_xxx",
        "status": "COMPLETED"
      }
    }
  }
}
```

**Security:**
- **Signature Header:** `x-square-signature`
- **Algorithm:** HMAC-SHA1 (older, less secure than SHA256)
- **Signed Payload:** `notification_url + payload`
- **Verification:**
  ```csharp
  var combinedPayload = webhookUrl + payload;
  using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(webhookSecret));
  var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(combinedPayload));
  var computedSignature = Convert.ToBase64String(hash);
  ```

### 4. Idempotency Approaches

#### Stripe: Idempotency-Key Header

```csharp
using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/payment_intents");
request.Headers.Add("Idempotency-Key", "order_123_payment_attempt_1");

// Stripe stores result for 24 hours
// Subsequent requests with same key return cached response (409 Conflict)
```

**Characteristics:**
- **Standard HTTP header:** `Idempotency-Key`
- **Scope:** All POST requests
- **Cache Duration:** 24 hours
- **Collision Handling:** Returns 409 Conflict with original response
- **Best Practice:** Use deterministic key like `{orderId}_{attemptNumber}`

#### Braintree: Duplicate Transaction Checking

```csharp
gateway.Transaction.Sale(new TransactionRequest
{
    Amount = 19.99M,
    PaymentMethodNonce = "nonce_xxx",
    Options = new TransactionOptionsRequest
    {
        // No explicit idempotency key—Braintree checks for duplicates
        // based on: customer_id + amount + payment_method + time window
    }
});
```

**Characteristics:**
- **No explicit idempotency key** (relies on gateway's duplicate detection)
- **Duplicate Detection:** Checks customer + amount + payment method within ~5 min window
- **Collision Handling:** Returns error with duplicate transaction ID
- **Limitations:** Short time window, not as reliable as explicit keys
- **Alternative:** Use `orderId` in custom fields and check before retrying

**Recommendation:** Implement application-level idempotency tracking for Braintree.

#### Square: Idempotency-Key Header

```csharp
POST /v2/payments
Headers:
  Authorization: Bearer {access_token}
  Square-Version: 2024-01-18

Body:
{
  "idempotency_key": "order_123_payment_attempt_1",  // Required field
  "amount_money": {"amount": 1999, "currency": "USD"},
  "source_id": "cnon_xxx"
}
```

**Characteristics:**
- **Required field in request body** (not header like Stripe)
- **Scope:** All mutation operations (payments, refunds, orders)
- **Cache Duration:** At least 24 hours
- **Collision Handling:** Returns original response if duplicate detected
- **Format:** UUID recommended, but any unique string works

### 5. Error Handling

All three gateways distinguish between permanent and transient failures:

#### Error Categories

| Error Category | Stripe | Braintree | Square | Retriable? |
|----------------|--------|-----------|--------|-----------|
| **Card Declined** | `card_error` (insufficient_funds, card_declined) | `processor_declined` (2000-2999) | `CARD_DECLINED` | ❌ No |
| **Invalid Request** | `invalid_request_error` | `validation_error` | `BAD_REQUEST` | ❌ No |
| **Gateway Error** | `api_error` | `gateway_error` | `INTERNAL_SERVER_ERROR` | ✅ Yes |
| **Rate Limit** | `rate_limit_error` (429) | `too_many_requests` | `RATE_LIMITED` (429) | ✅ Yes (with backoff) |
| **Network Error** | `processing_error` | `gateway_timeout` | `SERVICE_UNAVAILABLE` (503) | ✅ Yes |
| **Authentication** | `authentication_error` | `authentication_error` | `UNAUTHORIZED` (401) | ❌ No |

#### Stripe Error Response

```json
{
  "error": {
    "type": "card_error",
    "code": "card_declined",
    "decline_code": "insufficient_funds",
    "message": "Your card has insufficient funds."
  }
}
```

#### Braintree Error Response

```csharp
Result<Transaction> result = gateway.Transaction.Sale(request);

if (!result.IsSuccess())
{
    // Validation errors
    foreach (ValidationError error in result.Errors.DeepAll)
    {
        Console.WriteLine($"{error.Code}: {error.Message}");
    }
    
    // Processor response (card declined, etc.)
    if (result.Transaction?.ProcessorResponseCode != null)
    {
        // 2000-2999 = hard decline
        // 3000 = processor unavailable (retry)
    }
}
```

**Braintree Processor Response Codes:**
- `2000-2999`: Hard declines (insufficient funds, invalid card, etc.)
- `3000`: Processor unavailable (transient error, retry)
- `1000`: Approved

#### Square Error Response

```json
{
  "errors": [
    {
      "category": "PAYMENT_METHOD_ERROR",
      "code": "CARD_DECLINED",
      "detail": "The card was declined."
    }
  ]
}
```

**Square Error Categories:**
- `PAYMENT_METHOD_ERROR`: Card issues (declined, expired, invalid CVV)
- `INVALID_REQUEST_ERROR`: Bad request parameters
- `RATE_LIMIT_ERROR`: Too many requests
- `API_ERROR`: Square internal error (retry)

### 6. Customer & Payment Method Management

All three support storing customer payment methods for repeat purchases:

#### Stripe: Customer + PaymentMethod Pattern

```csharp
// 1. Create Customer
POST /v1/customers
{
  "email": "customer@example.com",
  "name": "John Doe",
  "metadata": {"user_id": "user_123"}
}
// Response: {"id": "cus_xxx"}

// 2. Create/Attach PaymentMethod
POST /v1/payment_methods
{
  "type": "card",
  "card": {"token": "tok_xxx"}  // From Stripe.js client-side
}
// Response: {"id": "pm_xxx"}

POST /v1/payment_methods/pm_xxx/attach
{
  "customer": "cus_xxx"
}

// 3. Charge saved PaymentMethod
POST /v1/payment_intents
{
  "amount": 1999,
  "currency": "usd",
  "customer": "cus_xxx",
  "payment_method": "pm_xxx",
  "off_session": true  // Indicates customer is not present
}
```

#### Braintree: Vault Pattern

```csharp
// 1. Create Customer
var customerRequest = new CustomerRequest
{
    Email = "customer@example.com",
    FirstName = "John",
    LastName = "Doe"
};
Result<Customer> result = gateway.Customer.Create(customerRequest);
// result.Target.Id = "customer_xxx"

// 2. Create PaymentMethod (stored in Vault)
var paymentMethodRequest = new PaymentMethodRequest
{
    CustomerId = "customer_xxx",
    PaymentMethodNonce = "nonce_from_client"  // From Braintree.js
};
Result<PaymentMethod> pmResult = gateway.PaymentMethod.Create(paymentMethodRequest);
// pmResult.Target.Token = "token_xxx"

// 3. Charge saved PaymentMethod
var transactionRequest = new TransactionRequest
{
    Amount = 19.99M,
    CustomerId = "customer_xxx",
    PaymentMethodToken = "token_xxx"  // Use token instead of nonce
};
Result<Transaction> txnResult = gateway.Transaction.Sale(transactionRequest);
```

**Key Difference:** Braintree calls stored payment methods "tokens" in the Vault, not `PaymentMethod` objects.

#### Square: Card on File Pattern

```csharp
// 1. Create Customer
POST /v2/customers
{
  "given_name": "John",
  "family_name": "Doe",
  "email_address": "customer@example.com"
}
// Response: {"customer": {"id": "customer_xxx"}}

// 2. Create Card on File
POST /v2/cards
{
  "idempotency_key": "card_123",
  "source_id": "cnon_xxx",  // Card nonce from Square.js
  "card": {
    "customer_id": "customer_xxx"
  }
}
// Response: {"card": {"id": "card_xxx"}}

// 3. Charge saved Card
POST /v2/payments
{
  "idempotency_key": "payment_123",
  "amount_money": {"amount": 1999, "currency": "USD"},
  "source_id": "card_xxx",  // Use card_id instead of nonce
  "customer_id": "customer_xxx",
  "location_id": "location_xxx"
}
```

### 7. Refund Handling

All three support full and partial refunds:

#### Stripe Refunds

```csharp
POST /v1/refunds
{
  "charge": "ch_xxx",  // Or payment_intent: "pi_xxx"
  "amount": 1999,      // Optional: omit for full refund
  "reason": "requested_by_customer"
}

// Response
{
  "id": "re_xxx",
  "amount": 1999,
  "status": "succeeded",  // or "pending", "failed"
  "charge": "ch_xxx"
}
```

**Refund Statuses:**
- `succeeded`: Refund completed
- `pending`: Being processed (bank delays)
- `failed`: Refund failed (insufficient funds in merchant account)

#### Braintree Refunds

```csharp
// Full refund
Result<Transaction> result = gateway.Transaction.Refund("txn_xxx");

// Partial refund
Result<Transaction> result = gateway.Transaction.Refund("txn_xxx", 10.00M);

// Response: creates new Transaction with type="credit"
{
  "id": "refund_txn_xxx",
  "type": "credit",
  "amount": "10.00",
  "refunded_transaction_id": "txn_xxx"
}
```

**Key Difference:** Refunds create new `Transaction` objects (type: credit) rather than separate `Refund` objects.

#### Square Refunds

```csharp
POST /v2/refunds
{
  "idempotency_key": "refund_123",
  "amount_money": {
    "amount": 1999,
    "currency": "USD"
  },
  "payment_id": "payment_xxx",
  "reason": "Customer requested refund"
}

// Response
{
  "refund": {
    "id": "refund_xxx",
    "status": "PENDING",  // PENDING → COMPLETED or FAILED
    "amount_money": {"amount": 1999, "currency": "USD"},
    "payment_id": "payment_xxx"
  }
}
```

**Refund Statuses:**
- `PENDING`: Being processed
- `COMPLETED`: Refund successful
- `REJECTED`: Payment method doesn't support refunds
- `FAILED`: Refund failed

### 8. Authentication Methods

#### Stripe: Bearer Token (Simple)

```csharp
request.Headers.Add("Authorization", $"Bearer {secretKey}");

// Secret key format: sk_test_xxx (test) or sk_live_xxx (production)
```

**Characteristics:**
- Single secret key for server-to-server
- `pk_xxx` publishable key for client-side
- No OAuth, no token refresh

#### Braintree: Public/Private Key + Client Token

```csharp
// Server-side: Public/Private key authentication
var gateway = new BraintreeGateway
{
    Environment = Braintree.Environment.SANDBOX,
    MerchantId = "merchant_id",
    PublicKey = "public_key",
    PrivateKey = "private_key"
};

// Client-side: Generate client token
string clientToken = gateway.ClientToken.Generate();
// Pass to Braintree.js for client-side tokenization
```

**Characteristics:**
- Three-key system (merchant ID + public + private)
- Client tokens expire (must be regenerated per session)
- More complex setup than Stripe

#### Square: OAuth 2.0 + Bearer Token

```csharp
// OAuth flow (for connecting multiple merchants)
// Or use personal access token for single merchant

request.Headers.Add("Authorization", $"Bearer {accessToken}");
request.Headers.Add("Square-Version", "2024-01-18");  // API versioning

// Access token format: sq0atp-xxx (production) or sq0atp-sandbox-xxx (sandbox)
```

**Characteristics:**
- OAuth 2.0 for marketplace apps (multiple merchants)
- Personal access tokens for single merchant
- API versioning via header (breaking changes between versions)
- Access tokens don't expire (unless revoked)

## Key Differences Summary

### API Design Philosophy

| Gateway | Design | Strengths | Weaknesses |
|---------|--------|-----------|------------|
| **Stripe** | RESTful, object-oriented | Clean, predictable, excellent docs | Verbose for simple use cases |
| **Braintree** | SDK-first (REST underneath) | Powerful SDK abstracts complexity | Harder to debug, less control |
| **Square** | REST, simplified | Easy to get started, fast integration | Less flexible, location_id requirement adds complexity |

### Transaction Model Philosophy

- **Stripe:** Separate intent vs. charge (clean separation of authorization intent from actual charge)
- **Braintree:** Single transaction through lifecycle (simpler mental model)
- **Square:** Single payment through lifecycle (similar to Braintree, but with version tokens for safety)

### Settlement Timing

- **Stripe:** Immediate capture (synchronous)
- **Braintree:** Batch settlement (typically next business day—async)
- **Square:** Immediate capture (synchronous)

### Best For

- **Stripe:** SaaS, subscriptions, marketplaces, developer-friendly integrations
- **Braintree:** E-commerce with PayPal integration, Venmo support
- **Square:** Brick-and-mortar retail, unified POS + online, small businesses

## Abstraction Layer Design Recommendations

### What to Generalize (Common Patterns)

✅ **All gateways support these—abstract them:**

1. **Two-Phase Commit (Authorize → Capture)**
   - Stripe: `capture_method: manual`
   - Braintree: `submit_for_settlement: false`
   - Square: `autocomplete: false`

2. **Idempotency**
   - Stripe: `Idempotency-Key` header
   - Braintree: Application-level tracking (no native support)
   - Square: `idempotency_key` field

3. **Webhook Notifications**
   - All three send async events
   - All require signature verification
   - All retry failed webhooks

4. **Customer Management**
   - All support creating customers
   - All support storing payment methods
   - All support charging saved payment methods

5. **Refunds**
   - All support full refunds
   - All support partial refunds
   - All have async refund processing

6. **Error Handling**
   - All distinguish transient vs. permanent errors
   - All provide decline codes
   - All support retry logic

### What to Keep Gateway-Specific

⚠️ **These vary too much—keep in implementations:**

1. **Authentication Setup**
   - Stripe: Single bearer token
   - Braintree: Three-key system + client token generation
   - Square: OAuth 2.0 or personal access token

2. **Client-Side Tokenization**
   - Stripe: `client_secret` for confirming PaymentIntent
   - Braintree: `payment_method_nonce` from Braintree.js
   - Square: `source_id` (card nonce) from Square.js

3. **Webhook Signature Verification**
   - Stripe: HMAC-SHA256 (timestamp + payload)
   - Braintree: RSA signature verification
   - Square: HMAC-SHA1 (url + payload)

4. **Transaction Status Mapping**
   - Each gateway has different status enums
   - Lifecycle transitions vary
   - Abstraction should map to common states

5. **Metadata/Custom Fields**
   - Stripe: `metadata` dictionary (flexible)
   - Braintree: `custom_fields` (limited to predefined fields)
   - Square: `note` and `reference_id` (less structured)

## Proposed IPaymentGateway Interface Enhancement

Based on this research, here's a recommended abstraction:

```csharp
namespace Payments.Gateways;

/// <summary>
/// Abstraction for payment gateway operations.
/// Implementations: StripePaymentGateway, BraintreePaymentGateway, SquarePaymentGateway
/// </summary>
public interface IPaymentGateway
{
    // ========================================
    // Core Payment Operations
    // ========================================
    
    /// <summary>
    /// Authorize payment (hold funds without capturing).
    /// Maps to: Stripe (PaymentIntent manual capture), Braintree (Transaction authorize),
    /// Square (Payment with autocomplete: false)
    /// </summary>
    Task<GatewayAuthorizationResult> AuthorizeAsync(
        AuthorizationRequest request,
        string idempotencyKey,
        CancellationToken ct);
    
    /// <summary>
    /// Capture previously authorized payment.
    /// Maps to: Stripe (capture PaymentIntent), Braintree (submitForSettlement),
    /// Square (complete Payment)
    /// </summary>
    Task<GatewayCaptureResult> CaptureAuthorizationAsync(
        string authorizationId,
        decimal? amount,  // null = full capture, value = partial capture
        string idempotencyKey,
        CancellationToken ct);
    
    /// <summary>
    /// Authorize and capture in one step (for immediate charges).
    /// Maps to: Stripe (PaymentIntent automatic capture), Braintree (Transaction sale),
    /// Square (Payment with autocomplete: true)
    /// </summary>
    Task<GatewayCaptureResult> ChargeAsync(
        AuthorizationRequest request,
        string idempotencyKey,
        CancellationToken ct);
    
    /// <summary>
    /// Cancel/void an authorized payment before capture.
    /// Maps to: Stripe (cancel PaymentIntent), Braintree (void Transaction),
    /// Square (cancel Payment)
    /// </summary>
    Task<GatewayCancelResult> CancelAuthorizationAsync(
        string authorizationId,
        string idempotencyKey,
        CancellationToken ct);
    
    /// <summary>
    /// Refund a captured payment (full or partial).
    /// Maps to: Stripe (create Refund), Braintree (refund Transaction),
    /// Square (create Refund)
    /// </summary>
    Task<GatewayRefundResult> RefundAsync(
        string transactionId,
        decimal? amount,  // null = full refund, value = partial refund
        string reason,
        string idempotencyKey,
        CancellationToken ct);
    
    // ========================================
    // Customer & Payment Method Management
    // ========================================
    
    /// <summary>
    /// Create a customer in the gateway.
    /// Maps to: Stripe (Customer), Braintree (Customer), Square (Customer)
    /// </summary>
    Task<GatewayCustomerResult> CreateCustomerAsync(
        string email,
        string? name,
        Dictionary<string, string> metadata,
        string idempotencyKey,
        CancellationToken ct);
    
    /// <summary>
    /// Save a payment method for a customer.
    /// Maps to: Stripe (attach PaymentMethod), Braintree (create PaymentMethod in Vault),
    /// Square (create Card)
    /// </summary>
    Task<GatewayPaymentMethodResult> SavePaymentMethodAsync(
        string customerId,
        string paymentMethodToken,  // Gateway-specific token from client-side
        string idempotencyKey,
        CancellationToken ct);
    
    /// <summary>
    /// List saved payment methods for a customer.
    /// </summary>
    Task<IReadOnlyList<GatewayPaymentMethod>> ListPaymentMethodsAsync(
        string customerId,
        CancellationToken ct);
    
    // ========================================
    // Webhook Handling
    // ========================================
    
    /// <summary>
    /// Verify webhook signature to prevent spoofing.
    /// Implementation varies by gateway (HMAC-SHA256, RSA, HMAC-SHA1).
    /// </summary>
    bool VerifyWebhookSignature(
        string payload,
        string signature,
        string webhookSecret);
    
    /// <summary>
    /// Parse webhook payload into standardized event.
    /// Maps gateway-specific events to common PaymentEvent types.
    /// </summary>
    Task<GatewayWebhookEvent?> ParseWebhookAsync(
        string payload,
        CancellationToken ct);
}

// ========================================
// Request/Response Models
// ========================================

public sealed record AuthorizationRequest(
    decimal Amount,
    string Currency,
    string CustomerId,
    string PaymentMethodId,
    Dictionary<string, string> Metadata);

public sealed record GatewayAuthorizationResult(
    bool Success,
    string? AuthorizationId,       // Gateway-specific ID (pi_xxx, txn_xxx, payment_xxx)
    PaymentStatus Status,           // Common status enum
    string? FailureReason,
    string? DeclineCode,
    bool IsRetriable);

public sealed record GatewayCaptureResult(
    bool Success,
    string? TransactionId,          // ID of completed transaction
    decimal AmountCaptured,
    PaymentStatus Status,
    string? FailureReason,
    bool IsRetriable);

public sealed record GatewayCancelResult(
    bool Success,
    string? AuthorizationId,
    PaymentStatus Status,
    string? FailureReason);

public sealed record GatewayRefundResult(
    bool Success,
    string? RefundId,
    decimal AmountRefunded,
    RefundStatus Status,
    string? FailureReason);

public sealed record GatewayCustomerResult(
    bool Success,
    string? CustomerId,
    string? FailureReason);

public sealed record GatewayPaymentMethodResult(
    bool Success,
    string? PaymentMethodId,
    string? Last4,
    string? Brand,
    string? FailureReason);

public sealed record GatewayPaymentMethod(
    string Id,
    string Type,
    string? Last4,
    string? Brand,
    int? ExpiryMonth,
    int? ExpiryYear);

public sealed record GatewayWebhookEvent(
    string EventId,
    string EventType,              // Normalized: "payment.succeeded", "payment.failed", etc.
    string GatewayEventType,       // Original: "payment_intent.succeeded", "transaction_settled", etc.
    string? TransactionId,
    PaymentStatus? Status,
    Dictionary<string, string> Metadata);

// ========================================
// Common Enums (Normalized Across Gateways)
// ========================================

public enum PaymentStatus
{
    /// <summary>Funds authorized but not captured</summary>
    Authorized,
    
    /// <summary>Payment being processed</summary>
    Processing,
    
    /// <summary>Payment completed successfully</summary>
    Succeeded,
    
    /// <summary>Payment failed (card declined, etc.)</summary>
    Failed,
    
    /// <summary>Authorization canceled/voided</summary>
    Canceled,
    
    /// <summary>Requires additional customer action (3D Secure)</summary>
    RequiresAction
}

public enum RefundStatus
{
    Pending,
    Succeeded,
    Failed
}
```

## Mapping Examples: How Each Gateway Implements the Interface

### Example 1: AuthorizeAsync

#### Stripe Implementation

```csharp
public async Task<GatewayAuthorizationResult> AuthorizeAsync(
    AuthorizationRequest request,
    string idempotencyKey,
    CancellationToken ct)
{
    try
    {
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/payment_intents");
        httpRequest.Headers.Add("Idempotency-Key", idempotencyKey);
        
        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("amount", ((int)(request.Amount * 100)).ToString()),
            new KeyValuePair<string, string>("currency", request.Currency.ToLowerInvariant()),
            new KeyValuePair<string, string>("customer", request.CustomerId),
            new KeyValuePair<string, string>("payment_method", request.PaymentMethodId),
            new KeyValuePair<string, string>("capture_method", "manual"),
            new KeyValuePair<string, string>("confirm", "true")
        });
        
        // Add metadata
        foreach (var kvp in request.Metadata)
        {
            content.Add($"metadata[{kvp.Key}]", kvp.Value);
        }
        
        httpRequest.Content = content;
        var response = await _httpClient.SendAsync(httpRequest, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        var intent = JsonSerializer.Deserialize<StripePaymentIntent>(json);
        
        if (!response.IsSuccessStatusCode)
        {
            var error = JsonSerializer.Deserialize<StripeError>(json);
            return new GatewayAuthorizationResult(
                Success: false,
                AuthorizationId: null,
                Status: PaymentStatus.Failed,
                FailureReason: error.Message,
                DeclineCode: error.DeclineCode,
                IsRetriable: error.Type == "api_error");
        }
        
        return new GatewayAuthorizationResult(
            Success: intent.Status == "requires_capture",
            AuthorizationId: intent.Id,
            Status: MapStripeStatus(intent.Status),
            FailureReason: null,
            DeclineCode: null,
            IsRetriable: false);
    }
    catch (HttpRequestException ex)
    {
        return new GatewayAuthorizationResult(
            Success: false,
            AuthorizationId: null,
            Status: PaymentStatus.Failed,
            FailureReason: ex.Message,
            DeclineCode: null,
            IsRetriable: true);
    }
}

private PaymentStatus MapStripeStatus(string status) => status switch
{
    "requires_capture" => PaymentStatus.Authorized,
    "processing" => PaymentStatus.Processing,
    "succeeded" => PaymentStatus.Succeeded,
    "canceled" => PaymentStatus.Canceled,
    "requires_action" => PaymentStatus.RequiresAction,
    _ => PaymentStatus.Failed
};
```

#### Braintree Implementation

```csharp
public async Task<GatewayAuthorizationResult> AuthorizeAsync(
    AuthorizationRequest request,
    string idempotencyKey,
    CancellationToken ct)
{
    try
    {
        // NOTE: Braintree doesn't have native idempotency—implement application-level check
        var existingTxn = await CheckForDuplicateTransactionAsync(idempotencyKey, ct);
        if (existingTxn != null)
        {
            return MapBraintreeResult(existingTxn);
        }
        
        var txnRequest = new TransactionRequest
        {
            Amount = request.Amount,
            CustomerId = request.CustomerId,
            PaymentMethodToken = request.PaymentMethodId,
            Options = new TransactionOptionsRequest
            {
                SubmitForSettlement = false  // Key: authorize only
            }
        };
        
        // Add metadata to custom fields
        foreach (var kvp in request.Metadata)
        {
            txnRequest.CustomFields.Add(kvp.Key, kvp.Value);
        }
        
        Result<Transaction> result = await Task.Run(
            () => _gateway.Transaction.Sale(txnRequest), ct);
        
        if (!result.IsSuccess())
        {
            var processorCode = result.Transaction?.ProcessorResponseCode;
            var isRetriable = processorCode == "3000";  // Processor unavailable
            
            return new GatewayAuthorizationResult(
                Success: false,
                AuthorizationId: null,
                Status: PaymentStatus.Failed,
                FailureReason: result.Message,
                DeclineCode: processorCode,
                IsRetriable: isRetriable);
        }
        
        // Store idempotency mapping
        await StoreIdempotencyMappingAsync(idempotencyKey, result.Target.Id, ct);
        
        return new GatewayAuthorizationResult(
            Success: result.Target.Status == TransactionStatus.AUTHORIZED,
            AuthorizationId: result.Target.Id,
            Status: MapBraintreeStatus(result.Target.Status),
            FailureReason: null,
            DeclineCode: null,
            IsRetriable: false);
    }
    catch (BraintreeException ex)
    {
        return new GatewayAuthorizationResult(
            Success: false,
            AuthorizationId: null,
            Status: PaymentStatus.Failed,
            FailureReason: ex.Message,
            DeclineCode: null,
            IsRetriable: true);
    }
}

private PaymentStatus MapBraintreeStatus(TransactionStatus status) => status switch
{
    TransactionStatus.AUTHORIZED => PaymentStatus.Authorized,
    TransactionStatus.AUTHORIZING => PaymentStatus.Processing,
    TransactionStatus.SETTLED => PaymentStatus.Succeeded,
    TransactionStatus.SETTLING => PaymentStatus.Processing,
    TransactionStatus.VOIDED => PaymentStatus.Canceled,
    _ => PaymentStatus.Failed
};
```

#### Square Implementation

```csharp
public async Task<GatewayAuthorizationResult> AuthorizeAsync(
    AuthorizationRequest request,
    string idempotencyKey,
    CancellationToken ct)
{
    try
    {
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/v2/payments");
        httpRequest.Headers.Add("Square-Version", _apiVersion);
        
        var body = new
        {
            idempotency_key = idempotencyKey,  // Native support
            amount_money = new
            {
                amount = (long)(request.Amount * 100),
                currency = request.Currency.ToUpperInvariant()
            },
            source_id = request.PaymentMethodId,
            customer_id = request.CustomerId,
            location_id = _locationId,  // Square requires location_id
            autocomplete = false,  // Key: manual capture
            note = string.Join("; ", request.Metadata.Select(kvp => $"{kvp.Key}={kvp.Value}"))
        };
        
        httpRequest.Content = new StringContent(
            JsonSerializer.Serialize(body),
            Encoding.UTF8,
            "application/json");
        
        var response = await _httpClient.SendAsync(httpRequest, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        
        if (!response.IsSuccessStatusCode)
        {
            var error = JsonSerializer.Deserialize<SquareErrorResponse>(json);
            var firstError = error.Errors.First();
            
            return new GatewayAuthorizationResult(
                Success: false,
                AuthorizationId: null,
                Status: PaymentStatus.Failed,
                FailureReason: firstError.Detail,
                DeclineCode: firstError.Code,
                IsRetriable: firstError.Category == "API_ERROR");
        }
        
        var paymentResponse = JsonSerializer.Deserialize<SquarePaymentResponse>(json);
        
        return new GatewayAuthorizationResult(
            Success: paymentResponse.Payment.Status == "APPROVED",
            AuthorizationId: paymentResponse.Payment.Id,
            Status: MapSquareStatus(paymentResponse.Payment.Status),
            FailureReason: null,
            DeclineCode: null,
            IsRetriable: false);
    }
    catch (HttpRequestException ex)
    {
        return new GatewayAuthorizationResult(
            Success: false,
            AuthorizationId: null,
            Status: PaymentStatus.Failed,
            FailureReason: ex.Message,
            DeclineCode: null,
            IsRetriable: true);
    }
}

private PaymentStatus MapSquareStatus(string status) => status switch
{
    "APPROVED" => PaymentStatus.Authorized,
    "PENDING" => PaymentStatus.Processing,
    "COMPLETED" => PaymentStatus.Succeeded,
    "CANCELED" => PaymentStatus.Canceled,
    "FAILED" => PaymentStatus.Failed,
    _ => PaymentStatus.Failed
};
```

## Recommendations for CritterSupply

### 1. Start with Stripe (Reference Implementation)

**Rationale:**
- Cleanest API design (REST-native, well-documented)
- Best developer experience (excellent error messages, test mode)
- Industry-standard patterns (perfect for learning reference architecture)
- Native idempotency support

**Action:** Implement `StripePaymentGateway` first, validate against order saga.

### 2. Abstract Common Patterns in IPaymentGateway

**Rationale:**
- Authorize/capture, refunds, customer management are universal
- Webhook patterns are similar enough to abstract
- Error handling maps cleanly across all three

**Action:** Use proposed `IPaymentGateway` interface above as starting point.

### 3. Handle Gateway-Specific Concerns in Implementations

**Don't over-abstract:**
- Authentication setup varies too much (bearer token vs. OAuth vs. three-key)
- Client-side tokenization is gateway-specific (handle in BFF/frontend)
- Webhook signature verification algorithms differ (implement per gateway)

### 4. Implement Application-Level Idempotency for Braintree

**Rationale:**
- Braintree's duplicate detection is unreliable (short time window)
- Other gateways have native support

**Action:** Create `IdempotencyStore` (Redis or Postgres) to track:
```csharp
public sealed record IdempotencyRecord(
    string IdempotencyKey,
    string GatewayTransactionId,
    DateTimeOffset CreatedAt);
```

### 5. Normalize Webhook Events to Domain Events

**Pattern:**
```
Gateway Webhook → GatewayWebhookEvent (normalized) → Domain Event (PaymentCaptured)
```

**Example:**
```csharp
// Stripe: payment_intent.succeeded → PaymentCaptured
// Braintree: transaction_settled → PaymentCaptured
// Square: payment.updated (status: COMPLETED) → PaymentCaptured
```

### 6. Consider Multi-Gateway Support

**Future-Proof Design:**
- Support multiple gateways simultaneously (different regions, redundancy)
- Store `gateway_type` in Payment aggregate
- Route refunds back to original gateway

**Implementation:**
```csharp
public interface IPaymentGatewayFactory
{
    IPaymentGateway GetGateway(PaymentGatewayType type);
}

public enum PaymentGatewayType
{
    Stripe,
    Braintree,
    Square
}
```

## Conclusion

All three payment gateways—Stripe, Braintree, and Square—support the core patterns needed for CritterSupply's Payments BC:

✅ **Universally Supported (Abstract These):**
- Two-phase commit (authorize → capture)
- Idempotency (though Braintree needs application-level)
- Webhooks for async notifications
- Customer and payment method management
- Refunds (full and partial)
- Distinguishing transient vs. permanent errors

⚠️ **Gateway-Specific (Keep in Implementations):**
- Authentication mechanisms
- Client-side tokenization patterns
- Webhook signature verification algorithms
- Transaction status enums and lifecycle
- Metadata/custom field formats

**Recommendation:** Start with Stripe as the reference implementation, design `IPaymentGateway` to abstract common patterns, and keep gateway-specific concerns in concrete implementations. This approach provides both learning value (clean Stripe patterns) and production readiness (easy to add Braintree/Square later).

The proposed interface above strikes the right balance between abstraction (common operations) and flexibility (gateway-specific implementations). It aligns well with CritterSupply's event-driven architecture and order saga orchestration.
