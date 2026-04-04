# M39.x Planning Session — Critter Stack Audit + Refresh Priorities

**Date:** 2026-04-04
**Participants:** @PSA, @QAE, @PO, @DOE
**Status:** Findings presented to Erik for M39.x decisions

---

## Executive Summary

This audit examined all 17 implemented backend bounded contexts against the documented Critter Stack patterns in `docs/skills/`. The codebase is in **good overall shape** — the M36.0 engineering quality pass was highly effective, and the newer BCs (Shopping, Listings, Marketplaces, Vendor Identity) are near-idiomatic. However, several older BCs and a few mid-lifecycle BCs show meaningful drift from current standards.

**The strongest finding:** Correspondence BC has 9 handlers that all call `session.Events.StartStream()` directly instead of returning `IStartStream` via `MartenOps.StartStream()` — the most concentrated idiom violation in the codebase. Pricing BC has 3 fat HTTP endpoints that mix infrastructure and business logic. Orders BC has 4 Checkout handlers using a workaround from M32.3 that may now be unnecessary with Wolverine 5.25+. Across all BCs, 14 redundant `SaveChangesAsync()` calls remain in EF Core BCs (Customer Identity, Backoffice Identity, Backoffice) that were missed by M36.0's Marten-focused sweep.

**For CritterSupply's mission as a reference architecture**, the priority is clear: anyone studying the Correspondence, Pricing, or Orders Checkout code would encounter patterns that contradict the documented best practices. A targeted refresh of these 3 areas would eliminate the "WTF? Oh, it was us." moments Erik is looking for.

---

## Section 1: Codebase State at Audit (from CURRENT-CYCLE.md)

| Aspect | Value |
|--------|-------|
| **Bounded contexts** | 19 total (17 backend, 2 frontend-only) |
| **Integration tests** | 139 (98 Marketplaces + 41 Listings + others) |
| **E2E scenarios** | 9 active on `@shard-3` |
| **Build errors** | 0 |
| **Next ADR** | 0058 |
| **Last milestone** | M38.1 (complete 2026-04-04) |
| **Last quality pass** | M36.0 (complete 2026-03-29) |
| **Test files** | 173 .cs test files + 24 .feature files |

**Known inherited items from M38.1:**
1. eBay orphaned draft background sweep — detection in place, cleanup deferred
2. `SemaphoreSlim` base class extraction — deferred until 4th adapter
3. Rate limiting / 429 marketplace-specific header handling

**Known inherited items from M36.0:**
1. VP Team Management `@wip` E2E scenarios (13 deferred)
2. Returns cross-BC saga tests (6 skipped, monitor)
3. Product Catalog `SaveChangesAsync` sweep (12 calls, opportunistic)

---

## Section 2: Critter Stack Idiom Audit (@PSA)

### Per-BC Findings Summary

| BC | Status | Tier 1 Issues | Tier 2 Issues | Tier 3 Issues | Refactor Scope |
|---|---|---|---|---|---|
| **Shopping** | ✅ Clean | 0 | 0 | 0 | None |
| **Orders** | ⚠️ Drift | 4 (Checkout `SaveChangesAsync` + `FetchForWriting`) | 0 | 0 | **M** |
| **Payments** | ✅ Clean | 0 | 0 | 0 | None |
| **Inventory** | ⚠️ Minor | 3 (`SaveChangesAsync`, fat endpoint, MD5 GUID) | 0 | 0 | **S-M** |
| **Fulfillment** | ⚠️ Minor | 2 (direct `StartStream` ×2) | 0 | 1 | **S** |
| **Returns** | ✅ Clean | 0 (3 advisory) | 0 | 0 | **S** (advisory) |
| **Customer Identity** | ⚠️ Minor | 5 (redundant `SaveChangesAsync`) | 0 | 0 | **S** |
| **Product Catalog** | ⚠️ Minor | 2 (direct `StartStream`, `Guid.NewGuid`) | 0 | 1 (legacy dual model) | **S** |
| **Listings** | ⚠️ Minor | 1 (direct `StartStream`) | 1 (bulk handler file) | 1 (no `[WriteAggregate]`) | **S-M** |
| **Marketplaces** | ⚠️ Minor | 5 (redundant `SaveChangesAsync`) | 1 (bulk handler file) | 0 | **S** |
| **Promotions** | ⚠️ Minor | 4 (manual load+append, return type) | 0 | 0 | **M** |
| **Pricing** | ⚠️ Drift | 6 (fat endpoints, manual load+append, missing snapshot) | 0 | 0 | **M** |
| **Correspondence** | 🔴 Significant | 9 (direct `StartStream` ×9), 2 (fat handler, double append) | 0 | 0 | **L** |
| **Vendor Identity** | ✅ Clean | 0 | 0 | 0 | None |
| **Vendor Portal** | ⚠️ Minor | 1 (missing `AutoApplyTransactions`) | 0 | 0 | **S** |
| **Backoffice Identity** | ⚠️ Minor | 8 (redundant `SaveChangesAsync`) | 0 | 0 | **S** |
| **Backoffice** | ⚠️ Minor | 2 (redundant `SaveChangesAsync`, direct append) | 0 | 0 | **S** |

### Tier 1 Findings: Critter Stack Idiom Compliance

#### 🔴 Critical — Correspondence BC (9 violations of Anti-Pattern #9)

**The single largest idiom violation in the codebase.** All 9 integration event handlers call `session.Events.StartStream<Message>()` directly instead of returning `IStartStream` via `MartenOps.StartStream()`:

| File | Line |
|------|------|
| `Correspondence/Notifications/OrderPlacedHandler.cs` | 46 |
| `Correspondence/Notifications/ShipmentDispatchedHandler.cs` | 51 |
| `Correspondence/Notifications/ShipmentDeliveredHandler.cs` | 57 |
| `Correspondence/Notifications/ShipmentDeliveryFailedHandler.cs` | 53 |
| `Correspondence/Notifications/ReturnApprovedHandler.cs` | 54 |
| `Correspondence/Notifications/ReturnCompletedHandler.cs` | 47 |
| `Correspondence/Notifications/ReturnDeniedHandler.cs` | 52 |
| `Correspondence/Notifications/ReturnExpiredHandler.cs` | 47 |
| `Correspondence/Notifications/RefundCompletedHandler.cs` | 55 |

**Additional Correspondence issues:**
- `SendMessageHandler.cs` (lines 62, 85, 125): Fat handler (140+ lines) mixing aggregate loading, provider calls, event appending, retry scheduling — Anti-Pattern #7
- `SendMessageHandler.cs`: Calls `session.Events.Append()` while also returning `OutgoingMessages` — mixed side-effects (Anti-Pattern #2 variant)
- `Guid.NewGuid()` used for Message stream IDs instead of `Guid.CreateVersion7()` (line 73, 105 in `Message.cs`)
- No `Snapshot<Message>` configured — every `AggregateStreamAsync<Message>()` replays full event stream
- Connection string uses `"marten"` key while most BCs use `"postgres"` — inconsistent naming

**Refactor scope: L (Large)** — 9 handler refactors + `SendMessageHandler` decomposition + snapshot config + stream ID migration

#### 🔴 High — Orders BC Checkout Handlers (4 violations)

Four Checkout handlers use a "Direct Implementation" workaround from M32.3 that mixes `FetchForWriting` + manual `AppendOne` + explicit `SaveChangesAsync()`:

| File | Lines | Issue |
|------|-------|-------|
| `Orders/Checkout/CompleteCheckout.cs` | 23, 46-47, 73 | **Outbox risk**: calls `SaveChangesAsync()` then returns `OutgoingMessages` — event persisted but integration message could be lost if outbox enrollment fails |
| `Orders/Checkout/ProvideShippingAddress.cs` | 46, 64-65 | Redundant `SaveChangesAsync()` (no outbox risk — returns `IResult` only) |
| `Orders/Checkout/ProvidePaymentMethod.cs` | 35, 48-49 | Same as above |
| `Orders/Checkout/SelectShippingMethod.cs` | 37, 51-52 | Same as above |

**Root cause:** M32.3 discovered that `[WriteAggregate]` silently fails with mixed route + body parameters. Wolverine 5.25+ added `[WriteAggregate("checkoutId")]` with explicit `FromRoute` identity resolution, which likely eliminates this limitation.

**Recommendation:** Spike `[WriteAggregate("checkoutId")]` on one handler. If it works, refactor all 4. `CompleteCheckout.cs` is highest priority due to outbox consistency risk.

**Refactor scope: M (Medium)** — spike + 4 handler refactors

#### ⚠️ Medium — Pricing BC Fat Endpoints (6 findings)

Pricing has 3 HTTP endpoints that are fat controllers mixing aggregate loading, business logic, event building, and `session.Events.Append()`:

| File | Lines | Issue |
|------|-------|-------|
| `Pricing/Pricing/SetBasePriceEndpoint.cs` | 41-104 | 85-line endpoint: manual load + append + mixed concerns |
| `Pricing/Pricing/SchedulePriceChangeEndpoint.cs` | 39-110 | 70+ lines: aggregate load + invariant check + append + `ScheduleAsync` |
| `Pricing/Pricing/CancelScheduledPriceChangeEndpoint.cs` | 16-62 | Same pattern, smaller |
| `Pricing/Pricing/SetInitialPriceHandler.cs` | 67-98 | Manual `AggregateStreamAsync()` + tuple return (Anti-Pattern #8) |
| `Pricing/Pricing/ChangePriceHandler.cs` | 43-92 | Same pattern as above |
| `Pricing.Api/Program.cs` | — | No `Snapshot<ProductPrice>` configured — stream replay on every read |

**Additional:** 4 `TODO` comments for JWT claim extraction (`SetBy: Guid.NewGuid()` placeholders)

**Refactor scope: M (Medium)** — decompose 3 endpoints into command+handler, add snapshot config

#### ⚠️ Medium — Promotions BC Handler Patterns (4 findings)

| File | Lines | Issue |
|------|-------|-------|
| `Promotions/Coupons/RedeemCouponHandler.cs` | 19, 45 | Manual `AggregateStreamAsync()` + `Append()` — should use `[WriteAggregate]` |
| `Promotions/Coupons/RevokeCouponHandler.cs` | 19, 49 | Same pattern |
| `Promotions/Promotions/RecordPromotionRedemptionHandler.cs` | 28, 63 | Same pattern |
| `Promotions/Promotions/ActivatePromotionHandler.cs` | 7-9 | Returns single event from `[WriteAggregate]` — should return `Events` collection |

**Refactor scope: M (Medium)** — 3 handlers to `[WriteAggregate]`, 1 return type fix

#### ⚠️ Low-Medium — Redundant `SaveChangesAsync()` Sweep (EF Core BCs)

M36.0 swept Marten-based BCs but missed EF Core BCs. 14 redundant calls remain:

| BC | Count | Files |
|---|---|---|
| Customer Identity | 5 | `CreateCustomer.cs`, `AddAddress.cs`, `UpdateAddress.cs`, `SetDefaultAddress.cs`, `GetAddressSnapshot.cs` |
| Backoffice Identity | 8 | `CreateBackofficeUser.cs`, `DeactivateBackofficeUser.cs`, `ChangeBackofficeUserRole.cs`, `ResetBackofficeUserPassword.cs`, `Login.cs`, `Logout.cs`, `RefreshToken.cs`, `ResetBackofficeUserPasswordEndpoint.cs` |
| Backoffice | 1 | `Notifications/OrderPlacedHandler.cs` |

**Refactor scope: S (Small)** — mechanical removal, single PR

#### ⚠️ Low-Medium — Marketplaces BC `SaveChangesAsync()` (5 calls)

| File | Line |
|------|------|
| `Marketplaces.Api/CategoryMappings/SetCategoryMapping.cs` | 58, 73 |
| `Marketplaces.Api/Marketplaces/RegisterMarketplace.cs` | 81 |
| `Marketplaces.Api/Marketplaces/DeactivateMarketplace.cs` | 41 |
| `Marketplaces.Api/Marketplaces/UpdateMarketplace.cs` | 52 |

**Refactor scope: S (Small)** — mechanical removal

#### ⚠️ Low — Direct `StartStream` in Other BCs

| BC | File | Line | Note |
|---|---|---|---|
| Fulfillment | `RequestFulfillment.cs` | 46 | Refactorable to `MartenOps.StartStream` |
| Fulfillment | `FulfillmentRequestedHandler.cs` | 50 | Justified by idempotency guard — accept as-is |
| Product Catalog | `CreateProduct.cs` | 87 | Refactorable |
| Product Catalog | `MigrateProduct.cs` | 54 | Refactorable |
| Listings | `CreateListing.cs` | 62 | Refactorable |
| Inventory | `ReceiveInboundStock.cs` | — | Fat endpoint + redundant `SaveChangesAsync` |

#### ⚠️ Low — `Guid.NewGuid()` Instead of `Guid.CreateVersion7()`

| BC | File | Line |
|---|---|---|
| Product Catalog | `CreateProduct.cs` | 60 |
| Product Catalog | `MigrateProduct.cs` | 33 |
| Correspondence | `Message.cs` (MessageFactory) | 73, 105 |
| Promotions | `GenerateCouponBatchHandler.cs` | 38 |

#### ⚠️ Low — Missing `AutoApplyTransactions()`

| BC | File | Impact |
|---|---|---|
| Vendor Portal | `VendorPortal.Api/Program.cs` | Low — BFF, mostly reads/SignalR |

#### Informational — Accepted Deviations

| BC | File | Issue | Justification |
|---|---|---|---|
| Returns | `RequestReturn.cs:139` | Direct `StartStream` | Multi-event creation flow (start + auto-approve in same tx) |
| Fulfillment | `FulfillmentRequestedHandler.cs:50` | Direct `StartStream` | Idempotency guard requires conditional creation |
| Fulfillment | `RecordDeliveryFailure.cs` | Double aggregate load in `Before()` | `[WriteAggregate]` returns 400 for missing streams, not 404 |
| Inventory | `ProductInventory.CombinedGuid()` | MD5 hash for composite keys | Intentional deterministic ID, but should be UUID v5 (migration risk) |
| Listings | `ProductSummaryHandlers.cs` | 9 handlers in one file | ACL handlers for same read model — logically cohesive |
| Marketplaces | `ProductSummaryHandlers.cs` | 4 handlers in one file | Same pattern as Listings |
| Product Catalog | Legacy `Product` document model | Dual-model (document + ES) | Migration bridge — retire after migration complete |

### Tier 2 Findings: Vertical Slice Organization

The codebase is **well-organized overall**. No `*ES` file suffixes remain. No bulk `*CommandHandlers.cs` files. Two `ProductSummaryHandlers.cs` files (Listings, Marketplaces) consolidate ACL handlers — acceptable due to logical cohesion.

**One area for improvement:** Listings BC write handlers (`ActivateListing`, `ApproveListing`, `PauseListing`, `ResumeListing`, `EndListing`, `SubmitListingForReview`) use imperative `session.Events.AggregateStreamAsync()` + `session.Events.Append()` instead of `[WriteAggregate]` parameter pattern. This misses Wolverine's concurrency control benefits. Scope: M.

### Tier 3 Findings: Structural Freshness

| Check | Result |
|-------|--------|
| `[Aggregate]` (old attribute) | ✅ Zero usages across entire codebase |
| `[WriteAggregate]` (current) | ✅ Used consistently in Shopping (11), Payments (2), Fulfillment (4+), Returns (7), Promotions (1) |
| Pre-`IStartStream` stream creation | ⚠️ 14 handlers across 5 BCs still use direct `session.Events.StartStream()` |

### @PSA's Proposed Scoping Options

#### Option A: Targeted BC Refresh (Recommended)
**Pick the 3 BCs with most drift; refresh thoroughly in one milestone.**

| BC | What | Sessions |
|---|---|---|
| **Correspondence** | 9 `StartStream` → `IStartStream`, decompose `SendMessageHandler`, add snapshot, fix stream IDs | 2-3 |
| **Pricing** | Decompose 3 fat endpoints, add snapshot, fix `[WriteAggregate]` pattern, TODO cleanup | 1-2 |
| **Orders (Checkout only)** | Spike `[WriteAggregate("checkoutId")]`, refactor 4 handlers, fix outbox risk | 1 |

**Total: 4-6 sessions.** Covers the highest-severity findings and the biggest "WTF" moments. Quick-win `SaveChangesAsync` sweep (EF Core BCs + Marketplaces) can be bundled as a warm-up session.

#### Option B: Broad Idiom Sweep
**Sweep all flagged issues across all BCs in one milestone.**

Everything in Option A, plus:
- `SaveChangesAsync` removal: Customer Identity (5), Backoffice Identity (8), Backoffice (1), Marketplaces (5)
- `Guid.NewGuid()` → `Guid.CreateVersion7()`: Product Catalog (2), Correspondence (2), Promotions (1)
- Direct `StartStream` → `IStartStream`: Fulfillment (1), Product Catalog (2), Listings (1), Inventory (1)
- Promotions handler pattern fix (3 handlers + 1 return type)
- Listings `[WriteAggregate]` migration (6 handlers)
- Vendor Portal `AutoApplyTransactions()` (1 line)

**Total: 8-12 sessions.** Comprehensive but large. Risk of milestone fatigue.

#### Option C: Opportunistic Only
**Address only issues that intersect with planned feature work.**

Keep a checklist of idiom violations. When touching a BC for feature work (e.g., Product Variants in Product Catalog), fix the idiom issues in that BC as part of the feature PR. No dedicated quality milestone.

**Pro:** Zero overhead. **Con:** Correspondence and Pricing may never get touched if no feature work targets them, and they remain poor reference material.

---

## Section 3: Test Coverage Audit (@QAE)

### Integration Test Coverage by BC

| BC | Test Project | Test Count | Assessment |
|---|---|---|---|
| **Marketplaces** | `Marketplaces.Api.IntegrationTests` | 98 | ✅ Excellent — all adapters covered |
| **Listings** | `Listings.Api.IntegrationTests` | 41 | ✅ Good coverage |
| **Backoffice** | `Backoffice.Api.IntegrationTests` | 95+ | ✅ Good coverage |
| **Orders** | `Orders.Api.IntegrationTests` | Present | ✅ Saga + checkout covered |
| **Payments** | `Payments.Api.IntegrationTests` | Present | ✅ Covered |
| **Shopping** | `Shopping.Api.IntegrationTests` | Present | ✅ Covered |
| **Customer Identity** | `Customers.Api.IntegrationTests` | Present | ✅ Covered |
| **Returns** | `Returns.Api.IntegrationTests` | 44 | ⚠️ 6 skipped cross-BC saga tests (since M36.0) |
| **Product Catalog** | `ProductCatalog.Api.IntegrationTests` | 48 | ✅ ES migration covered |
| **Vendor Identity** | `VendorIdentity.Api.IntegrationTests` | Present | ✅ Covered |
| **Vendor Portal** | `VendorPortal.Api.IntegrationTests` | 86 | ✅ Covered |
| **Inventory** | `Inventory.Api.IntegrationTests` | Present | ✅ Covered |
| **Fulfillment** | `Fulfillment.Api.IntegrationTests` | Present | ✅ Covered |
| **Pricing** | `Pricing.Api.IntegrationTests` | Present | ✅ Covered |
| **Promotions** | `Promotions.Api.IntegrationTests` | Present | ✅ Covered |
| **Correspondence** | `Correspondence.Api.IntegrationTests` | Present | ⚠️ Coverage depth unknown |
| **Backoffice Identity** | `BackofficeIdentity.Api.IntegrationTests` | Present | ✅ Covered |

### Known Test Gaps

1. **Returns cross-BC saga tests (6 skipped)** — Documented since M36.0 as a Wolverine saga persistence issue. Still skipped. Should re-evaluate against Wolverine 5.27.0 to see if the upstream issue is resolved.

2. **eBay orphaned draft background sweep** — Detection mechanism exists (`CheckSubmissionStatusAsync` returns `IsFailed` for UNPUBLISHED status). No test for the sweep/cleanup mechanism itself because the mechanism doesn't exist yet.

3. **`SemaphoreSlim` token caching double-check lock pattern** — All 3 marketplace adapters implement identical token caching. Not unit-tested in isolation — tested implicitly via adapter integration tests. If base class extraction happens, add dedicated concurrency tests.

4. **VP Team Management E2E scenarios (13 `@wip`)** — Deferred since M36.0. These test the Blazor WASM UI for team management features. Blocked on frontend completion.

5. **Correspondence BC** — Test depth should be verified. Given the significant idiom drift found by @PSA, test coverage may need to be bolstered alongside the handler refactors.

### Test Pattern Quality

- **`TestAuthHandler`** is used consistently across all test fixtures since M36.0 ✅
- **5 `TODO` comments** remain in test files across the codebase
- **`FakeHttpClientFactory`** in Marketplaces tests — documented as intentional for bypassing Polly in unit-style adapter tests; real resilience tested via `AdapterResilienceTests.cs` which builds a full `ServiceCollection`

---

## Section 4: Options for Erik (@PO)

### Lens 1: CritterSupply as a Reference Architecture

**What practitioners need to see:**

| Pattern | BC | Status | Value as Reference |
|---------|-----|--------|-------------------|
| Event-sourced aggregate with full lifecycle | Shopping (Cart), Orders (Checkout + Order), Returns | ✅ Excellent | High — Shopping is the gold standard |
| Saga orchestration (multi-BC) | Orders (OrderSaga + OrderDecider) | ✅ Excellent | High — decider pattern is exemplary |
| Document store + CRUD | Marketplaces, Customer Identity | ✅ Good | Medium |
| External service integration | Marketplaces (3 adapters) | ✅ Excellent | High — real OAuth, API calls, resilience |
| BFF + SignalR real-time | Storefront, Vendor Portal | ✅ Good | Medium |
| EF Core + Wolverine | Customer Identity, Vendor Identity, Backoffice Identity | ✅ Good | Medium |
| Fat endpoint anti-pattern (negative example) | Pricing, Correspondence | 🔴 Poor | **Negative** — practitioners will copy the wrong pattern |
| Transactional outbox compliance | Most BCs | ⚠️ Mixed | Orders Checkout has outbox risk |

**Assessment:** The biggest risk to CritterSupply's reference mission is not missing features — it's that someone studying Pricing or Correspondence will learn anti-patterns. Fixing these BCs is higher-value than adding new features for M39.x.

**Future features not yet warranted:**
- **Product Variants (`ProductFamily` aggregate)** — valuable reference but premature before current quality issues are resolved
- **Search BC** — requires significant design work (event modeling session needed), defer to M40+
- **Dynamic Consistency Boundary** — no current use case in the codebase, defer until Product Variants

### Lens 2: Deferred Items Assessment

| Item | Source | Assessment | Recommendation |
|------|--------|------------|----------------|
| eBay orphaned draft background sweep | M38.1 | **Wait** — Detection exists, cleanup mechanism needs design. Not urgent until production eBay volume exists. | M40+ |
| `SemaphoreSlim` base class extraction | M38.1 | **Wait** — Only 3 adapters. Extract when 4th adapter added or during quality pass. | Opportunistic |
| Rate limiting / 429 headers | M38.1 | **Wait** — Polly retry covers the common case. Marketplace-specific headers are optimization. | M40+ |
| VP Team Management `@wip` E2E (13) | M36.0 | **Wait** — Frontend-dependent. Not backend priority per Erik's direction. | Post-M39 |
| Returns cross-BC saga tests (6 skipped) | M36.0 | **Now** — Re-evaluate against Wolverine 5.27.0. If upstream fix landed, unskip. If not, document why. | M39.x (verify only) |
| Product Catalog legacy `Product` model | M35.0 | **Wait** — Migration bridge is functional. Remove when migration bootstrap path is confirmed. | Opportunistic |
| Correspondence real provider integration | M28.0 | **Wait** — SendGrid/Twilio integration is feature work, not quality work. | M40+ |

### Recommended M39.x Sequencing

**M39.0 — Critter Stack Idiom Refresh (4-6 sessions)**
1. Session 0: `SaveChangesAsync` sweep (EF Core BCs + Marketplaces) — warm-up, 14+5 removals
2. Sessions 1-2: Correspondence BC full refresh (9 `StartStream`, `SendMessageHandler` decomposition, snapshot, stream IDs)
3. Session 3: Pricing BC refresh (3 fat endpoints → command+handler, snapshot, TODO cleanup)
4. Session 4: Orders Checkout spike + refactor (`[WriteAggregate("checkoutId")]`)
5. Session 5 (if time): Quick wins across remaining BCs (`Guid.CreateVersion7`, `StartStream` fixes in Fulfillment/Product Catalog/Listings)

**M39.1 (if warranted) — Broader Sweep**
- Promotions handler pattern fixes
- Listings `[WriteAggregate]` migration
- Inventory `ReceiveInboundStock` refactor
- Returns saga test re-evaluation

**M40.x — Feature Work Resumes**
- Product Variants / `ProductFamily` aggregate
- Search BC (requires event modeling session first)

---

## Section 5: Infrastructure + CI Audit (@DOE)

### Docker Compose Coverage

All 19 BCs have Docker Compose entries with appropriate profiles. ✅

| Component | Profile | Status |
|-----------|---------|--------|
| Infrastructure (Postgres, RabbitMQ, Jaeger) | `infrastructure`, `all`, `ci` | ✅ |
| All API services | `all` + per-BC profiles | ✅ |
| Storefront Web, Vendor Portal Web, Backoffice Web | `all` + per-BC | ✅ |

### Aspire AppHost Coverage

`CritterSupply.AppHost` exists and wires resources. Should be verified that all 19 BCs are registered — especially the newer BCs (Listings, Marketplaces, Promotions, Pricing) that were added after the AppHost was initially created.

**Severity: P2** — Aspire is optional workflow; Docker Compose is primary.

### ServiceDefaults Consistency

`CritterSupply.ServiceDefaults/Extensions.cs` provides shared OpenTelemetry, health checks, and service discovery configuration. All API projects should reference this package.

**Action needed:** Verify all `.Api.csproj` files reference `CritterSupply.ServiceDefaults`. Any BC not referencing it would miss OpenTelemetry instrumentation and health check endpoints.

**Severity: P1** — affects observability coverage.

### OpenTelemetry / Observability

Wolverine has built-in OpenTelemetry support. Jaeger is configured in the `infrastructure` Docker Compose profile. The ServiceDefaults project provides shared OTLP configuration.

**Gap:** No dead-letter queue monitoring is configured. Failed messages go to Wolverine's built-in dead-letter mechanism but there's no alerting or dashboard for them.

**Severity: P2** — important for production readiness, not urgent for reference architecture.

### CI/CD Workflows

CI workflow files were not found in `.github/workflows/` in the checked-out branch. This may be due to the branch being a shallow clone or workflows existing only on `main`. The problem statement references CI Run #881 (green), confirming CI exists and passes.

**Severity: P2** — verify all test projects are included in CI matrix.

### Rate Limiting / 429 Handling

Current state: Polly resilience pipelines handle transient failures (retry + circuit breaker) across all 3 marketplace adapters. Marketplace-specific rate limit headers (Amazon's `x-amzn-RateLimit-Limit`, eBay's `X-RateLimit-Remaining`) are not consumed.

**Assessment:** This is an **adapter concern**, not infrastructure. The adapters should read rate limit headers and adjust request pacing. This is optimization work, not correctness work.

**Severity: P2** — defer to M40+ unless marketplace API throttling becomes a problem.

### Connection String Naming Inconsistency

| BC | Connection String Key |
|---|---|
| Most BCs | `"postgres"` |
| Correspondence | `"marten"` |
| Some older BCs | `"marten"` (legacy) |

**Severity: P2** — cosmetic inconsistency, both keys resolve correctly in all environments.

### Infrastructure Gaps Summary

| Gap | Severity | Recommendation |
|-----|----------|----------------|
| Verify ServiceDefaults referenced by all APIs | **P1** | Audit in M39.x Session 0 |
| Dead-letter queue monitoring | P2 | Defer to production readiness milestone |
| Aspire AppHost completeness | P2 | Verify, fix if missing BCs |
| Connection string naming consistency | P2 | Standardize to `"postgres"` during quality pass |
| Rate limiting headers | P2 | Defer to M40+ |
| CI workflow coverage verification | P2 | Verify all test projects in matrix |

---

## Section 6: Cross-Cutting Observations (All Agents)

### Observation 1: EF Core BCs Were Missed by M36.0

M36.0's `SaveChangesAsync` sweep focused on Marten-based BCs. The EF Core BCs (Customer Identity, Backoffice Identity) still have 13 redundant `SaveChangesAsync()` calls. This is because `AutoApplyTransactions()` works with both Marten and EF Core, but the M36.0 sweep was Marten-focused. A single PR can clean this up.

### Observation 2: Correspondence is the Oldest Unreformed BC

Correspondence was built early (M28.0) and has never been through a quality pass. It shows every pattern that later milestones corrected in other BCs: direct `StartStream`, instance handlers (not static), `Guid.NewGuid()` for stream IDs, no snapshot, fat handler. It's the most valuable single-BC refresh target for the reference architecture mission.

### Observation 3: Pricing Was Built in a "Fat Endpoint" Style

Pricing's 3 HTTP endpoints (`SetBasePriceEndpoint`, `SchedulePriceChangeEndpoint`, `CancelScheduledPriceChangeEndpoint`) follow a controller-style pattern that predates the Wolverine compound handler convention. The TODO comments (`// TODO: Get from JWT claim`) suggest these were scaffolded quickly and never revisited. Decomposing into command+handler would make Pricing a proper reference for scheduled-event patterns.

### Observation 4: Test Coverage Correlates with Idiom Compliance

BCs with the most tests (Marketplaces: 98, Backoffice: 95+, Product Catalog: 48, Returns: 44) are also the most idiom-compliant. BCs with fewer or shallower tests (Correspondence, Pricing) show more drift. This isn't causal — both are symptoms of how recently each BC was built or refreshed.

### Observation 5: `Guid.NewGuid()` vs `Guid.CreateVersion7()` Is a Codebase-Wide Paper Cut

5 files across 4 BCs still use `Guid.NewGuid()` for new stream/entity IDs. This is a trivial fix (literal text replacement) but each instance is a "WTF" for anyone reading the code against the documented convention.

### Observation 6: Missing Marten Snapshots

Correspondence and Pricing configure no snapshots for their aggregates. As event streams grow, `AggregateStreamAsync()` will replay increasing numbers of events. This won't be noticeable with small datasets but will degrade in any reference deployment running at scale.

---

## Section 7: Open Questions for Erik

1. **Should M39.x be a dedicated quality milestone (Option A/B) or opportunistic only (Option C)?**
   - @PSA recommends Option A (targeted: Correspondence + Pricing + Orders Checkout). This covers the highest-severity findings in 4-6 sessions without milestone fatigue.
   - Option B is comprehensive but risks 8-12 sessions of quality work with no new features.
   - Option C is zero-overhead but leaves Correspondence and Pricing as poor reference material indefinitely.

2. **Should the `SaveChangesAsync` sweep cover EF Core BCs?**
   - 13 calls in Customer Identity + Backoffice Identity were missed by M36.0. They're functionally harmless (redundant, not broken) but inconsistent with the codebase standard. Recommend bundling as a 30-minute warm-up task in Session 0.

3. **Should Listings write handlers migrate to `[WriteAggregate]`?**
   - Current imperative pattern works but misses concurrency control. This is a scope question: include in M39.x (adds ~1 session) or defer?

4. **Are the 6 skipped Returns cross-BC saga tests still blocked?**
   - These were skipped due to a Wolverine saga persistence issue documented in M36.0. Wolverine has had multiple releases since (5.25 → 5.27). Should we re-evaluate in M39.x?

5. **Is Product Catalog's legacy `Product` document model ready to retire?**
   - All 14 handlers are event-sourced. The legacy model and `MigrateProduct` handler remain. Does Erik want to schedule retirement, or keep the migration bridge indefinitely?

6. **Should Correspondence's connection string key be standardized from `"marten"` to `"postgres"`?**
   - Minor inconsistency. Fix during Correspondence refresh or leave as-is?

7. **Priority of Promotions handler pattern fixes?**
   - 3 handlers use manual load+append instead of `[WriteAggregate]`, and `ActivatePromotionHandler` has a potentially incorrect return type. Include in M39.x or defer to M39.1?

---

*End of planning session document. All findings are audit-only — no production code was changed.*
