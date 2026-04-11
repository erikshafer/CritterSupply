# M38.0 — Session 2: Polly Resilience + DeactivateListingAsync

## Where We Are

Session 1 is complete and merged (PR #509). Read
`docs/planning/milestones/m38-0-session-1-retrospective.md` before writing a single line.

**Session 1 deliverables:**
- P-7: ADR 0055 — Walmart submission status polling architecture
- P-1: `WalmartMarketplaceAdapter.CheckSubmissionStatusAsync` — real feed status polling (PROCESSED/ERROR/RECEIVED/INPROGRESS)
- P-2: `ListingApprovedHandler` Walmart path schedules `CheckWalmartFeedStatus` via `bus.ScheduleAsync()` instead of publishing `MarketplaceListingActivated` immediately
- P-3: `CheckWalmartFeedStatusHandler` — escalating delay schedule, 10-attempt max, publishes outcome on resolution
- P-4: `Listings.Api/Program.cs` — `DeclareExchange` bindings for both marketplace outcome exchanges → `listings-marketplace-outcome-events` queue
- P-5: `MarketplaceListingActivatedHandler` — `Submitted → Live` with idempotency guards; also added `ListingActivated` + `ListingEnded` publish routes to `Listings.Api/Program.cs` (pre-existing gap found during implementation)
- P-6: `MarketplaceSubmissionRejectedHandler` — `Submitted → Ended (SubmissionRejected)` with idempotency guards
- Integration tests: 120 total (79 Marketplaces + 41 Listings), +15 from Session 1
- Build: 0 errors, 12 warnings (all pre-existing)

**Important Session 1 learnings to carry forward:**

1. **`DeclareExchange` is the correct Wolverine.RabbitMQ 5.27 API** for binding an exchange to a queue. The `BindExchange` extension on `RabbitMqListenerConfiguration` does not exist. When Session 2 adds any new exchange bindings, use the same `DeclareExchange` pattern confirmed in P-4.

2. **`bus.ScheduleAsync()` cannot be intercepted in unit tests** via `SendAsync`/`PublishAsync` captures when called on a plain `IMessageBus` stub. For the Polly retry tests in this session, prefer Wolverine's tracking test harness (if available in the test project) or verify observable behavior (no exceptions, correct return values) rather than asserting the scheduling side-effect.

---

Read before starting:
- `docs/planning/milestones/m38-0-session-1-retrospective.md` — Session 1 learnings above plus P-4 wiring pattern
- `docs/planning/milestones/m38-0-plan.md` — Sections 2 (decisions), 3 (scope table), 4 (Session 2 detail)
- `docs/decisions/0055-submission-status-polling-architecture.md` — polling decisions are locked; do not revisit
- `src/Marketplaces/Marketplaces/Adapters/AmazonMarketplaceAdapter.cs` — deactivation skeleton with comment documenting the PATCH approach
- `src/Marketplaces/Marketplaces/Adapters/WalmartMarketplaceAdapter.cs` — deactivation skeleton with comment documenting the retire approach
- `src/Marketplaces/Marketplaces/Adapters/EbayMarketplaceAdapter.cs` — deactivation skeleton with comment documenting the withdraw approach
- `tests/Marketplaces/Marketplaces.Api.IntegrationTests/Helpers/MarketplaceAdapterTestHelpers.cs` — shared test doubles; `FakeHttpMessageHandler` has URL-keyed response support
- `docs/skills/httpclient.md` — **mandatory for P-8 and P-9/P-10/P-11** — documents the named client pattern, `AddResilienceHandler` attachment point, per-request header rules (`HttpRequestMessage`, never `DefaultRequestHeaders`), explicit `Timeout` requirement, and the anti-pattern table. Session 2 touches all of these surfaces.
- `docs/skills/wolverine-message-handlers.md` — mandatory before touching any handler
- `docs/decisions/adr-0052-amazon-spapi-authentication.md`, `0053`, `0054` — per-adapter rate limits documented here

---

## What This Session Does

Session 2 delivers P-8 through P-12 from the M38.0 plan.

| Item | BC | Description |
|------|----|-------------|
| P-8 | Marketplaces | Polly retry + circuit breaker via `Microsoft.Extensions.Http.Resilience` on all three real adapter `HttpClient` pipelines |
| P-9 | Marketplaces | `WalmartMarketplaceAdapter.DeactivateListingAsync` — retire item via feed |
| P-10 | Marketplaces | `AmazonMarketplaceAdapter.DeactivateListingAsync` — PATCH delete purchasable_offer |
| P-11 | Marketplaces | `EbayMarketplaceAdapter.DeactivateListingAsync` — POST offer withdraw |
| P-12 | ADR | ADR 0056 — Marketplace Adapter Resilience Patterns |

**Session 3 items (admin UI buttons, E2E unblock) are not in scope.** Do not begin P-13 or P-14
even if time permits.

---

## Guard Rails — Non-Negotiable

1. **ADR 0056 must be committed before any Polly code is written.** The ADR establishes the
   per-adapter rate limits, retry count, 401 special case, and circuit breaker scope that all
   Polly configuration references. Same pattern as ADR 0055 before Session 1 polling code.

2. **The 401 retry special case must be handled correctly.** A 401 on the API endpoint (expired
   token) should trigger a token cache clear and a single retry. A 401 on the token exchange
   endpoint (invalid credentials) must NOT be retried — that would just burn vault reads.
   The Polly pipeline applies to the named `HttpClient`, which means it wraps all HTTP calls
   including token exchange. The retry predicate must distinguish API 401s (retry) from token
   endpoint 401s (propagate immediately). See Guard Rail 2 Detail below.

3. **The `externalListingId` prefix convention must be preserved in deactivation.** Each adapter
   receives an `externalListingId` with its own prefix (`amzn-{sku}`, `wmrt-{feedId}`,
   `ebay-{offerId}`). Strip the prefix correctly — as confirmed by the existing adapter
   `CheckSubmissionStatusAsync` patterns — before using the raw ID in API calls. Deactivation
   takes the same ID format as activation.

4. **Stub adapters are not touched.** P-8 (Polly) applies only to the real adapter `HttpClient`
   pipelines registered under `UseRealAdapters`. `StubAmazonAdapter`, `StubWalmartAdapter`, and
   `StubEbayAdapter` do not use `IHttpClientFactory` and must not be modified.

5. **Replace the `DeactivateListing_ReturnsFalse` skeleton tests.** All three adapter test classes
   have a test asserting `false`. Once P-9/P-10/P-11 deliver real implementations, those tests
   must be replaced with real scenario tests, not left alongside the new tests.

6. **`OutgoingMessages` for integration events. `IMessageBus` injection only for `bus.ScheduleAsync()`.**
   The deactivation handlers (if any, see Note on Deactivation Trigger below) follow the same
   constraints established in previous sessions.

7. **Commit each item separately.** P-12 (ADR) is one commit. P-8 (Polly setup) is one commit.
   P-9, P-10, P-11 are one commit each. Each adapter's test update is one commit.

---

### Guard Rail 2 Detail: 401 Retry Special Case

The Polly pipeline is registered on the named `HttpClient` via `AddResilienceHandler()`. This
wraps ALL requests through that client, including the token exchange POST. The retry predicate
must be URL-aware or status-code-aware:

```csharp
// Pattern: retry on 429 and 5xx, but NOT on 401 from the token endpoint
pipeline.AddRetry(new HttpRetryStrategyOptions
{
    MaxRetryAttempts = 3,
    BackoffType = DelayBackoffType.Exponential,
    Delay = TimeSpan.FromSeconds(1),
    ShouldHandle = static args =>
        new ValueTask<bool>(args.Outcome is { Exception: null } &&
            args.Outcome.Result?.StatusCode is HttpStatusCode.TooManyRequests or
            >= HttpStatusCode.InternalServerError &&
            // Do not retry 401 — token cache clears on 401 from API calls, not from the
            // retry pipeline. Retrying 401 here would just hit the endpoint again with the
            // same expired token rather than allowing the token refresh path to run.
            args.Outcome.Result?.StatusCode != HttpStatusCode.Unauthorized)
});
```

For the 401 → token refresh → retry pattern on API calls, that logic lives inside each adapter's
`GetAccessTokenAsync` and the API call method itself, not in the Polly pipeline. The Polly pipeline
handles transient failures (429, 5xx). The adapter handles auth failures (401 on API calls) by
clearing the token cache and re-entering `GetAccessTokenAsync`.

Document this design in ADR 0056.

---

## Note on Deactivation Trigger

P-9/P-10/P-11 implement `DeactivateListingAsync` on the adapters. The question of **when to
call these methods** — i.e., what Listings BC event or Marketplaces BC handler triggers
deactivation — is not in scope for Session 2. The M38.0 plan (Q4 decision) defers the full
deactivation wire-up to the bidirectional feedback design. Session 2 delivers the adapter
implementation only. The callers (future handlers in Marketplaces BC that react to `ListingEnded`
or `ListingPaused` events from Listings BC) are Session 3 candidates or M38.1 scope depending
on capacity.

---

## Execution Order

```
P-12: Author and commit ADR 0056 — establishes rate limits, retry count, 401 handling,
      circuit breaker scope before any Polly code is written
  ↓
P-8: Register Polly resilience pipelines on AmazonSpApi, WalmartApi, EbayApi HttpClients
     in Marketplaces.Api/Program.cs
  ↓
[P-8 tests — @QAE: 429 retry, 5xx retry, circuit breaker tests]
  ↓
P-9: WalmartMarketplaceAdapter.DeactivateListingAsync — retire item feed
  ↓
[P-9 tests — replace DeactivateListing_ReturnsFalse with real scenarios]
  ↓
P-10: AmazonMarketplaceAdapter.DeactivateListingAsync — PATCH delete purchasable_offer
  ↓
[P-10 tests — replace DeactivateListing_ReturnsFalse with real scenarios]
  ↓
P-11: EbayMarketplaceAdapter.DeactivateListingAsync — POST offer withdraw
  ↓
[P-11 tests — replace DeactivateListing_ReturnsFalse with real scenarios]
```

P-9/P-10/P-11 are independent of each other and can proceed in parallel if @PSA and @QAE
split the work by adapter after P-8 is committed.

---

## Mandatory Session Bookends

**First act:** Run `dotnet build` from solution root — confirm 0 errors, 12 warnings unchanged.
Run `dotnet test` on `Marketplaces.Api.IntegrationTests` (79 passing) and
`Listings.Api.IntegrationTests` (41 passing). Record as session baseline.

**Last acts — all required:**

**1. Commit `docs/planning/milestones/m38-0-session-2-retrospective.md`**

Must cover:
- P-8: Where the Polly pipelines are registered in `Program.cs`, which package was added,
  the retry strategy (count, backoff type, delay), circuit breaker scope chosen (per-adapter),
  and how the 401 special case is handled (in the Polly predicate vs. in the adapter)
- P-9: How Walmart deactivation works — which API (feed-based vs. direct Retire Item endpoint),
  how the `wmrt-{feedId}` prefix is stripped, what the request body looks like
- P-10: How Amazon deactivation works — the PATCH endpoint, what `amzn-{sku}` strips to, what
  the `patches` array looks like
- P-11: How eBay deactivation works — the withdraw endpoint, how `ebay-{offerId}` strips to
  the offer ID
- Which tests replaced the `DeactivateListing_ReturnsFalse` skeletons and what they verify
- Test counts per affected BC at session start and session close
- Build state at session close (errors, warnings)
- CI run number confirming green
- What Session 3 should pick up (admin UI buttons, E2E unblock — P-13 and P-14)

**2. Update `CURRENT-CYCLE.md`**

Add "M38.0 Session 2 Progress" block. Record completed items (P-8 through P-12), updated
test counts, and CI run number. Update Last Updated timestamp.

**3. Run and record the full test suite**

Both the retrospective and CURRENT-CYCLE.md must reference the same CI run number.

---

## Roles

### @PSA — Principal Software Architect

Primary owner of P-12 (ADR 0056), P-8 (Polly setup), P-9/P-10/P-11 (deactivation).

---

**P-12 — ADR 0056: Marketplace Adapter Resilience Patterns**

Location: `docs/decisions/0056-marketplace-adapter-resilience-patterns.md`

Cover:
- Why `Microsoft.Extensions.Http.Resilience` (wrapping Polly) was chosen over raw Polly
  (idiomatic for .NET + `IHttpClientFactory` integration)
- Per-adapter rate limits documented in ADRs 0052–0054:
    - Amazon SP-API: 5 req/s, burst 10
    - Walmart Marketplace API: 10 req/s
    - eBay Sell API: varies by endpoint; document the `sell.inventory` endpoint limit
- Retry strategy: count, backoff, which HTTP status codes trigger retry (429, 5xx) vs.
  which do not (401)
- Why 401 is excluded from the Polly retry predicate (the adapter handles 401 via token
  refresh; Polly retrying a 401 would hit the same endpoint with the same stale token)
- Circuit breaker: per-adapter scope (each named `HttpClient` has its own breaker), opens
  after N consecutive failures within a time window
- What this does NOT cover (Amazon/eBay `CheckSubmissionStatusAsync` skeleton, deactivation
  caller wiring)

---

**P-8 — Polly resilience pipelines on all three real adapter `HttpClient` registrations**

Location: `src/Marketplaces/Marketplaces.Api/Program.cs`, inside the `if (useRealAdapters)` block

Add `Microsoft.Extensions.Http.Resilience` to `Marketplaces.csproj` if not already present
(it may already be there given it was in the bin directory from Listings.Api). Verify before
adding.

**Read `docs/skills/httpclient.md` before writing any P-8 code.** It documents the named
client + `AddResilienceHandler` attachment pattern, the `Timeout` requirement (the default
100s is far too permissive — set an explicit 30s on each named client in this same
`AddHttpClient` call), and the `DefaultRequestHeaders` anti-pattern. The existing adapter
code follows the skill already; P-8 extends that same registration block.

Register a resilience pipeline on each named `HttpClient`:

```csharp
builder.Services.AddHttpClient("AmazonSpApi")
    .AddResilienceHandler("amazon-resilience", pipeline =>
    {
        pipeline.AddRetry(new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            BackoffType = DelayBackoffType.Exponential,
            Delay = TimeSpan.FromSeconds(1),
            ShouldHandle = static args =>
                new ValueTask<bool>(
                    args.Outcome.Exception is HttpRequestException ||
                    (args.Outcome.Result?.StatusCode is
                        HttpStatusCode.TooManyRequests or
                        >= HttpStatusCode.InternalServerError))
        });
        pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
        {
            HandledEventsAllowedBeforeBreaking = 5,
            BreakDuration = TimeSpan.FromSeconds(30)
        });
    });
```

Apply the same pattern for `"WalmartApi"` and `"EbayApi"` with appropriately named handler keys
(`"walmart-resilience"`, `"ebay-resilience"`). The retry strategy is identical for all three —
the per-adapter rate limits are enforced by each marketplace's API server (a 429 triggers
the retry policy). A future session may tune per-adapter delay values if needed; for M38.0
use a uniform 1-second exponential backoff.

**Note on `HttpStatusCode` comparison:** `>= HttpStatusCode.InternalServerError` does not
compile as a pattern match in C# (it's not a constant). Use explicit values instead:
`HttpStatusCode.InternalServerError`, `HttpStatusCode.BadGateway`, `HttpStatusCode.ServiceUnavailable`,
`HttpStatusCode.GatewayTimeout` — or check `(int)statusCode >= 500`.

---

**P-9 — `WalmartMarketplaceAdapter.DeactivateListingAsync`**

The `externalListingId` format: `wmrt-{feedId}`. Note that the feed ID used during submission
is the Walmart `feedId` from the submission response. However, for item **retirement**, Walmart
provides two approaches:

1. **Feed-based retirement:** POST a retire feed to `/v3/feeds?feedType=RETIRE_ITEM` with
   the item SKU. This is the feed-based equivalent of the submission approach and is preferred
   for consistency.
2. **Direct Retire Item API:** Less commonly documented. The feed approach is safer.

Use approach 1 (RETIRE_ITEM feed). The item to retire is identified by SKU, not by feed ID.
**The `externalListingId` carries `wmrt-{feedId}` — but the retirement feed needs the SKU.**
This is a gap: `DeactivateListingAsync` only receives `externalListingId`, not the original SKU.

**Design decision for ADR 0056:** `DeactivateListingAsync` receives only `externalListingId`.
For Walmart, the submission feed ID (`wmrt-{feedId}`) does not carry the SKU. The caller
(a future `ListingEndedHandler` in Marketplaces BC) will need to pass the SKU or the
externalListingId must encode more information.

**For M38.0, implement the most pragmatic approach:**

Option A: Log the gap and return `false` with a clear error reason (defer to M38.1 with design).
Option B: The `WalmartMarketplaceAdapter` constructor accepts an `IDocumentSession` to look up
the listing's SKU from Marketplaces BC's own documents — but this couples the adapter to Marten.

**Recommended: Option A with a logged gap** — document it in ADR 0056 as a known limitation.
The deactivation contract is complete at the interface level; the caller pattern (what data the
caller provides) is deferred to the handler design. This is honest about the architecture state.

If you prefer Option B, implement it and document the Marten dependency in ADR 0056.

**Regardless of the approach chosen:** Replace the existing skeleton's `return Task.FromResult(false)`
with an implementation that logs the specific reason (gap vs. success) and return an accurate `bool`.

---

**P-10 — `AmazonMarketplaceAdapter.DeactivateListingAsync`**

The `externalListingId` format: `amzn-{sku}`. Strip the prefix to get the raw SKU.

Amazon deactivation via SP-API PATCH endpoint:
```
PATCH /listings/2021-08-01/items/{sellerId}/{sku}?marketplaceIds={marketplaceId}
Content-Type: application/json
Authorization: Bearer {accessToken}
x-amz-access-token: {accessToken}

{
  "productType": "PRODUCT",
  "patches": [
    {
      "op": "delete",
      "path": "/attributes/purchasable_offer",
      "value": []
    }
  ]
}
```

The PATCH with `op: delete` on `purchasable_offer` removes the listing from the marketplace
without deleting the ASIN. This is the standard SP-API deactivation pattern per ADR 0052.

Retrieve `sellerId` and `marketplaceId` from `IVaultClient` (same vault paths as `SubmitListingAsync`).
Return `true` on 2xx. Return `false` on 4xx/5xx with logging. Follow the same exception handling
pattern as `SubmitListingAsync` (catch non-cancellation exceptions, log, return `false`).

---

**P-11 — `EbayMarketplaceAdapter.DeactivateListingAsync`**

The `externalListingId` format: `ebay-{offerId}`. Strip the prefix to get the raw offer ID.

eBay offer withdrawal:
```
POST /sell/inventory/v1/offer/{offerId}/withdraw
Authorization: Bearer {accessToken}
X-EBAY-C-MARKETPLACE-ID: {marketplaceId}
```

A successful response is `200 OK` with a `listingId` in the body. Return `true` on 2xx.
Return `false` on 4xx/5xx with logging.

Retrieve `marketplaceId` from `IVaultClient` (`ebay/marketplace-id`). Reuse the cached access
token from `GetAccessTokenAsync`. Follow the same exception pattern as `SubmitListingAsync`.

Note: A withdrawn offer can be republished if needed (it is not deleted). This is the correct
approach for a listing that is paused or ended — it removes the listing from the marketplace
without destroying the offer data.

**Skills:** `wolverine-message-handlers`, `marten-event-sourcing`

---

### @QAE — QA Engineer

Primary owner of integration tests for P-8 through P-11.

**After P-8 is committed — Polly resilience tests**

New file (or extend existing): `tests/Marketplaces/Marketplaces.Api.IntegrationTests/AdapterResilienceTests.cs`

Use `FakeHttpMessageHandler.EnqueueResponse` from `MarketplaceAdapterTestHelpers` to stage
error responses and verify retry behavior. For each adapter (Amazon, Walmart, eBay), at minimum:

- `Adapter_RetriesOnTooManyRequests_ThenSucceeds` — stage a 429 followed by a 200; verify
  the submission result is success (Polly retried and the 200 landed)
- `Adapter_RetriesOnInternalServerError_ThenSucceeds` — stage a 500 followed by a 200; same assertion
- `Adapter_DoesNotRetryOn401` — stage a 401 from the API endpoint; verify it is NOT retried
  (submission fails immediately without a second request)

Three adapters × three scenarios = 9 tests is the target. If `FakeHttpMessageHandler`'s
response queue is consumed per-request, a 429 → 200 test requires two responses enqueued.
Verify the `FakeHttpMessageHandler` supports this before writing the tests; extend it if needed.

**After P-9/P-10/P-11 are committed — deactivation tests**

For each adapter, in the existing adapter test class:

- **Replace** `DeactivateListing_ReturnsFalse` (the skeleton assertion) with a real test:
    - `DeactivateListing_ReturnsTrue_WhenApiSucceeds` — stage a 2xx response; assert `true`
    - `DeactivateListing_ReturnsFalse_WhenApiFails` — stage a 4xx response; assert `false`
    - `DeactivateListing_BuildsCorrectRequest` — verify the HTTP method, URL shape, and key
      headers on the deactivation request (use `FakeHttpMessageHandler.SentRequests`)

Three adapters × three scenarios = 9 tests.

**Note on Walmart deactivation:** If @PSA implements Option A (log gap, return false), then
`DeactivateListing_ReturnsTrue_WhenApiSucceeds` cannot pass yet — document this in the
retrospective and write the test as `@wip` or comment it out with an explanation.

**Total test count target for Session 2:** +15 to +20 (9 resilience + 9 deactivation, minus
the 3 skeleton tests being replaced = net +15). Record exact counts.

**Skills:** `critterstack-testing-patterns`

---

## Session Habits

Commit frequently and atomically in execution order. ADR commits before Polly code. Each
adapter's deactivation is its own commit. Each adapter's test update is its own commit.

Suggested commit message format: `M38.0 P-N: {BC} — {description}`

Examples:
- `M38.0 P-12: docs — ADR 0056 marketplace adapter resilience patterns`
- `M38.0 P-8: Marketplaces.Api — Polly resilience pipelines on AmazonSpApi, WalmartApi, EbayApi HttpClients`
- `M38.0 P-8 tests: Marketplaces.IntegrationTests — Polly retry and circuit breaker scenarios`
- `M38.0 P-9: Marketplaces — WalmartMarketplaceAdapter.DeactivateListingAsync`
- `M38.0 P-9 tests: Marketplaces.IntegrationTests — Walmart deactivation scenarios`
- `M38.0 P-10: Marketplaces — AmazonMarketplaceAdapter.DeactivateListingAsync`
- `M38.0 P-10 tests: Marketplaces.IntegrationTests — Amazon deactivation scenarios`
- `M38.0 P-11: Marketplaces — EbayMarketplaceAdapter.DeactivateListingAsync`
- `M38.0 P-11 tests: Marketplaces.IntegrationTests — eBay deactivation scenarios`

The most important quality constraint for this session: **the 401 special case must be
correctly handled.** A Polly pipeline that retries 401 responses will cause repeated vault
reads and token exchange calls on an already-invalid token, producing N×vault reads before
failing. Verify the retry predicate explicitly excludes `HttpStatusCode.Unauthorized` and
document the design in ADR 0056 before any Polly code is committed.

After Session 2, the M38.0 definition of done items 4 and 5 are complete. Session 3
(admin UI buttons + E2E unblock) closes items 6 and 7. Session 4 is milestone closure.
