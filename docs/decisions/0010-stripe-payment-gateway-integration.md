# ADR 0010: Stripe Payment Gateway Integration with Webhook-Driven Event Handling

**Status:** ✅ Accepted

**Date:** 2026-02-18

## Context

CritterSupply's Payments BC currently uses a `StubPaymentGateway` for development and testing. To serve as a complete reference architecture for event-driven systems, we need a realistic payment gateway integration that demonstrates:

1. **External API integration patterns** — How to integrate with third-party REST APIs
2. **Event-driven async handling** — Using webhooks for eventual consistency
3. **Idempotency and retry patterns** — Critical for financial transactions
4. **Two-phase commit alignment** — Matching our Order saga (authorize → capture)
5. **Production-ready error handling** — Graceful degradation, retriable vs non-retriable failures

Stripe was chosen as the reference gateway because:
- Industry-standard API design (RESTful, idempotent, webhook-driven)
- Well-documented payment patterns (PaymentIntents, two-phase commit)
- Robust test mode for development (no real charges)
- Strong developer tooling (CLI for webhook testing, test cards)

## Decision

We will implement a **Stripe payment gateway integration** with the following architecture:

### 1. Core Implementation

**StripePaymentGateway** will implement `IPaymentGateway` interface:
- `AuthorizeAsync()` → `POST /v1/payment_intents` (capture_method: manual)
- `CaptureAuthorizedAsync()` → `POST /v1/payment_intents/{id}/capture`
- `RefundAsync()` → `POST /v1/refunds`

**Configuration-driven provider selection:**
```csharp
if (builder.Environment.IsDevelopment() || useStub)
{
    builder.Services.AddSingleton<IPaymentGateway, StubPaymentGateway>();
}
else
{
    builder.Services.AddHttpClient<IPaymentGateway, StripePaymentGateway>();
}
```

### 2. Webhook Integration (Critical for Production)

**Webhook endpoint:** `/api/webhooks/stripe`

**Key events handled:**
- `payment_intent.succeeded` → Publish `PaymentCaptured` integration message
- `payment_intent.payment_failed` → Publish `PaymentFailed` integration message
- `charge.refunded` → Publish `RefundCompleted` integration message

**Security:**
- HMAC-SHA256 signature verification (Stripe-Signature header)
- Webhook secret stored in user secrets (dev) or environment variables (prod)

**Deduplication:**
- Store processed Stripe event IDs in Marten (document store)
- Return 200 OK for duplicate events without reprocessing

### 3. Idempotency Pattern

**Idempotency-Key header** sent with all Stripe API requests:
- Format: `"auth-{paymentId}"` (authorize), `"capture-{authorizationId}"` (capture), `"refund-{transactionId}-{amount}"` (refund)
- Deterministic keys enable safe retries (Stripe returns cached response for duplicate keys)
- Idempotency keys stored in `PaymentAttempt` documents (audit trail)

### 4. Metadata for Correlation

**Stripe PaymentIntent metadata:**
```json
{
  "payment_id": "guid",
  "order_id": "guid",
  "customer_id": "guid"
}
```

Enables correlation between Stripe events and CritterSupply aggregates in webhook handlers.

## Rationale

### Why Webhooks Over Polling?

**✅ Webhooks (Chosen):**
- **Guaranteed delivery** — Stripe retries failed webhooks (exponential backoff, 3 days)
- **Low latency** — Immediate notification (vs polling every N seconds)
- **Handles async payment methods** — 3D Secure, bank transfers (can take minutes/hours)
- **Network failure resilience** — If HTTP response is lost, webhook ensures eventual delivery

**❌ Polling (Rejected):**
- Requires background job (adds complexity)
- Delays payment confirmation (polling interval)
- Increased API load (100+ req/sec in high traffic)
- Misses events if polling job is down

### Why Two-Phase Commit (Authorize → Capture)?

Aligns with CritterSupply's Order saga:
```
1. Authorize payment (hold funds)
2. Reserve inventory
3. Capture payment (only if inventory confirmed)
```

This prevents:
- Charging customers for out-of-stock items
- Holding funds indefinitely (authorization expires after 7 days)
- Inventory reservation without payment guarantee

### Why Stripe Over Other Gateways?

| Criterion | Stripe | Braintree | Square |
|-----------|--------|-----------|--------|
| API Design | ✅ RESTful, modern | ✅ GraphQL option | ✅ REST |
| Webhooks | ✅ Comprehensive | ✅ Good | ⚠️ Limited |
| Test Mode | ✅ Excellent | ✅ Good | ⚠️ Basic |
| Documentation | ✅ Industry-leading | ✅ Good | ⚠️ Adequate |
| CLI Tooling | ✅ Best-in-class | ❌ None | ❌ None |
| Two-Phase Commit | ✅ Native (manual capture) | ✅ Vault flow | ⚠️ Limited |

Stripe's developer experience and comprehensive webhooks make it ideal for a reference architecture.

## Consequences

### Positive

1. **Production-Ready Pattern** — Developers learn real-world payment gateway integration
2. **Webhook-Driven Architecture** — Demonstrates eventual consistency in distributed systems
3. **Testability** — Stripe CLI enables local webhook testing without external dependencies
4. **Idempotency Example** — Shows critical pattern for financial operations
5. **Reference Quality** — Industry-standard API design (other gateways follow similar patterns)
6. **Graceful Degradation** — Stub gateway enables offline development (no API keys required)

### Negative

1. **Additional Complexity** — Webhook endpoint, signature verification, deduplication logic
2. **Test Environment Setup** — Developers need Stripe CLI for full local testing
3. **Configuration Management** — User secrets (dev) vs environment variables (prod) must be documented
4. **Webhook Reliability** — Local development requires tunneling (ngrok, Stripe CLI forward)
5. **Rate Limiting** — Must implement exponential backoff for production traffic

### Mitigation Strategies

**Complexity:**
- Comprehensive skill guide: `skills/stripe-payment-gateway.md` (TBD)
- Working code examples in spike document

**Test Setup:**
- Document Stripe CLI installation in README
- Provide docker-compose for local webhook forwarding (future enhancement)

**Configuration:**
- Clear appsettings.json examples
- User secrets commands in spike doc

**Webhook Reliability:**
- Idempotent handlers (safe to reprocess)
- Event deduplication (Stripe event ID storage)
- Structured logging for troubleshooting

**Rate Limiting:**
- Exponential backoff implementation
- Rate limit monitoring (future: alerts for 429 responses)

## Alternatives Considered

### Alternative 1: Braintree (PayPal)

**Pros:**
- Good webhook support
- Strong fraud protection
- PayPal integration (if needed)

**Cons:**
- Less comprehensive documentation
- No CLI tooling (harder local testing)
- GraphQL API (adds learning curve for some developers)

**Verdict:** Stripe's developer experience wins for reference architecture.

### Alternative 2: Square

**Pros:**
- Simple API
- Good for point-of-sale integration

**Cons:**
- Webhooks limited to basic events
- Less comprehensive test mode
- Smaller developer community

**Verdict:** Not robust enough for demonstrating production patterns.

### Alternative 3: Continue with StubPaymentGateway Only

**Pros:**
- Simplest (no external dependencies)
- Fast tests (no API calls)

**Cons:**
- ❌ Doesn't demonstrate real-world API integration
- ❌ Misses webhook-driven event handling
- ❌ Incomplete reference architecture
- ❌ Developers don't learn idempotency, retries, signature verification

**Verdict:** Insufficient for CritterSupply's educational mission.

## Implementation Plan

### Phase 1: Foundation (2026-02-19 to 2026-02-25)
- Create `StripePaymentGateway` class
- Implement authorize/capture/refund operations
- Add configuration (appsettings.json, user secrets)
- Unit tests (existing `StubPaymentGateway` tests)
- Integration tests (Stripe test mode)

### Phase 2: Webhooks (2026-02-26 to 2026-03-03)
- Create `/api/webhooks/stripe` endpoint
- Implement HMAC-SHA256 signature verification
- Handle `payment_intent.succeeded`, `payment_intent.payment_failed`, `charge.refunded`
- Webhook deduplication (store Stripe event IDs)
- Local testing with Stripe CLI

### Phase 3: Production Hardening (2026-03-04 to 2026-03-10)
- Idempotency key storage (`PaymentAttempt` documents)
- Webhook event audit trail (`ProcessedWebhookEvent` documents)
- Rate limiting with exponential backoff
- Structured logging (correlation IDs, event types)
- Monitoring queries (failed payments, processing times)

### Phase 4: Documentation (2026-03-11 to 2026-03-17)
- Update `CONTEXTS.md` (Payments BC integration flows)
- Create skill guide: `skills/stripe-payment-gateway.md`
- Add Stripe setup to README
- Manual test checklist for Stripe integration
- BDD scenarios for payment workflows (optional)

## References

- **Research Spike:** `docs/planning/spikes/stripe-api-integration.md`
- **Existing Interface:** `src/Payments/Payments/Processing/IPaymentGateway.cs`
- **Existing Stub:** `src/Payments/Payments/Processing/StubPaymentGateway.cs`
- **CONTEXTS.md:** Payments BC section (lines 289-362)
- **External Service Pattern:** `skills/external-service-integration.md`
- [Stripe Payment Intents Docs](https://stripe.com/docs/payments/payment-intents) (conceptual)
- [Stripe Webhooks Guide](https://stripe.com/docs/webhooks) (conceptual)
