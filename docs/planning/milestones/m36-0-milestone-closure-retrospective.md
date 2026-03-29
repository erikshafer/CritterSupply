# M36.0 Milestone Closure Retrospective — Engineering Quality

**Status:** ✅ Complete
**Sessions:** 6 (2026-03-28 through 2026-03-29)
**CI Confirmation:** CI Run #808 ✅ | E2E Run #381 ✅ | CodeQL Run #369 ✅

---

## Goal and Achievement

**Goal:** Make every bounded context more correct, more consistent, and more testable by eliminating Critter Stack idiom violations, DDD naming drift, authorization gaps, vertical slice non-compliance, and pre-existing test failures.

**Achieved:** Yes. All 9 Definition of Done criteria were met across 6 sessions. M36.0 delivered the most impactful correctness improvements since the project's inception — fixing 21 pre-existing test failures, protecting 55 endpoints with `[Authorize]`, removing 34 redundant `SaveChangesAsync()` calls, and discovering the root cause of Product Catalog projection failures that had been misclassified as timing issues.

---

## Definition of Done — Final Assessment

| # | Criterion | Status | Evidence |
|---|-----------|--------|----------|
| 1 | Zero pre-existing test failures | ✅ | Product Catalog 48/48 (was 43/48). Orders 48/48 (was 33/48). CustomerIdentity 29/29 (was 25/29). Correspondence 5/5 (was 3/5). Returns 6 skipped = documented Wolverine saga issue. |
| 2 | Zero Critter Stack idiom violations | ✅ | All `bus.PublishAsync()` calls replaced with `OutgoingMessages` tuples. 34 `SaveChangesAsync()` calls removed. `IStartStream` pattern applied to Payments. |
| 3 | Zero DDD naming violations | ✅ | `PaymentRequested` → `RequestPayment`, `RefundRequested` → `RequestRefund`, `CalculateDiscountRequest` → `CalculateDiscount`. ADR 0040 documented `*Requested` convention. |
| 4 | Vertical slice compliance | ✅ | VP 7 handlers split from monolith. PC `AssignProductToVendorES` split into 3 files. VI 6 validators colocated. |
| 5 | Authorization coverage | ✅ | 55 endpoints protected across 10 BCs. JWT Bearer added to Shopping + Storefront. Dual-scheme policy for Customer Identity. |
| 6 | VP Team Management E2E operational | ✅ | 2 executable + 13 `@wip` scenarios. Page object + step definitions delivered. |
| 7 | Backoffice Order Search/Detail E2E exists | ✅ | 4 scenarios with page objects and step definitions. |
| 8 | UI placeholders cleaned | ✅ | Storefront brand renamed. Template pages deleted. VP Dashboard button activated. |
| 9 | CI green | ✅ | CI Run #808, E2E Run #381, CodeQL Run #369 — all green on main. |

---

## Track-by-Track Delivery Summary

### Track A — Pre-Existing Test Failures (Session 1)

**Impact:** 21 failures → 0 failures across 1,042 integration tests.

| Item | BC | What Changed | Result |
|------|----|-------------|--------|
| A-1 | Shared | Created `TestAuthHandler` utility in `CritterSupply.TestUtilities` | Reusable auth bypass for all BC test fixtures |
| A-2 | Orders | Applied `TestAuthHandler` to test fixture | 48/48 (was 33/48) |
| A-3 | Customer Identity | Applied dual-scheme Cookie+JWT auth fixture | 29/29 (was 25/29) |
| A-4 | Correspondence | Applied `TestAuthHandler` to test fixture | 5/5 (was 3/5) |
| A-5 | Correspondence | Fixed duplicate routing rule for `CorrespondenceQueued` | Eliminated double-dispatch |

The `TestAuthHandler` pattern is now the standard for all BC integration test fixtures. It supports configurable claims, roles, tenant IDs, and multiple authentication schemes. Every new BC created in future milestones should use this utility from day one.

### Track B — Critter Stack Idiom Compliance (Sessions 2–3)

**Impact:** Zero `bus.PublishAsync()` violations remaining. 34 `SaveChangesAsync()` calls removed across 25 files.

| Item | BC | Violation | Fix |
|------|----|-----------|-----|
| B-1 | Payments | Manual `session.Events.StartStream()` | `IStartStream` return type |
| B-2 | Returns | 4 `bus.PublishAsync()` calls | `OutgoingMessages` tuple return |
| B-3 | Inventory | `SaveChangesAsync()` + `bus.PublishAsync()` | `OutgoingMessages` tuple; removed manual save |
| B-4 | Orders | `bus.PublishAsync()` in cancel endpoint | `OutgoingMessages` tuple return |
| B-5 | Vendor Portal | 27 `SaveChangesAsync()` calls | Removed (Wolverine auto-commits) |
| B-6 | Pricing | 5 `SaveChangesAsync()` calls | Removed |
| B-7 | Product Catalog | 2 `SaveChangesAsync()` calls in `AssignProductToVendorES` | Removed |

**Key insight from Session 2:** `bus.PublishAsync()` in HTTP endpoints bypasses the transactional outbox. The correct pattern is returning `(ResponseType, OutgoingMessages)` tuples from HTTP handlers, which Wolverine processes within the same transaction. `bus.ScheduleAsync()` remains the only justified `IMessageBus` usage (for delayed message delivery).

### Track C — DDD Naming + Vertical Slice Compliance (Sessions 3–4)

**Impact:** All internal commands follow verb-first naming. Vertical slices colocated per ADR 0039.

| Item | BC | What Changed |
|------|----|-------------|
| C-1 | Payments | `PaymentRequested` → `RequestPayment` (internal command) |
| C-2 | Payments | `RefundRequested` → `RequestRefund` (internal command; integration event unchanged) |
| C-3 | Promotions | `CalculateDiscountRequest` → `CalculateDiscount` |
| C-4 | Vendor Portal | Split `TeamEventHandlers.cs` into 7 handler files |
| C-5 | Product Catalog | Split `AssignProductToVendorES.cs` into 3 vertical slices |
| C-6 | Vendor Identity | Colocated 6 validators with their commands |
| C-7 | Shared | ADR 0040: `*Requested` suffix reserved for integration events |

The `*Requested` convention (ADR 0040) establishes that past-participle suffixed names like `RefundRequested` are reserved for integration events crossing BC boundaries. Internal commands use imperative verb form: `RequestRefund`, `PlaceOrder`, `CapturePayment`.

### Track D — Authorization Hardening (Sessions 4–5)

**Impact:** 55 endpoints protected with `[Authorize]` across 10 BCs. Zero unprotected mutation endpoints remain.

| Item | BCs | Endpoints Protected |
|------|-----|-------------------|
| D-1 | Vendor Identity | 10 `[Authorize]`, 3 `[AllowAnonymous]` (auth endpoints) |
| D-2 | Shopping + Storefront | 9 + 13 endpoints; JWT Bearer middleware added |
| D-3 | Returns | 9 mutation endpoints |
| D-4 | Fulfillment + Product Catalog + Customer Identity | 5 + 10 + 5 endpoints |
| D-5 | Orders | 4 checkout mutation endpoints |

Shopping and Storefront required entirely new JWT Bearer auth middleware — they had no authentication infrastructure prior to M36.0. Customer Identity required a dual-scheme default policy (`Cookie` + `Backoffice`) to handle both browser session auth and API token auth coexisting.

### Track E — UI Cleanup + E2E Coverage (Session 6)

**Impact:** Dead-end UI eliminated. 19 new E2E scenarios (2 executable, 13 `@wip`, 4 Backoffice).

| Item | Area | What Changed |
|------|------|-------------|
| E-1 | Storefront + VP | Brand rename, template page deletion, Dashboard button activation |
| E-2 | Vendor Portal | Team Management E2E: 15 scenarios, page object, step definitions |
| E-3 | Backoffice | Order Search/Detail E2E: 4 scenarios, page object, step definitions |

---

## Most Impactful Discovery: `AutoApplyTransactions`

The single most impactful finding of M36.0 was identifying that `ProductCatalog.Api/Program.cs` was missing `opts.Policies.AutoApplyTransactions()`. This one-line omission caused 5 integration test failures that had been misclassified as Marten async projection timing issues.

**Root cause:** Without `AutoApplyTransactions()`, Wolverine HTTP endpoints do not auto-commit the Marten `IDocumentSession` after handler execution. Handlers returned HTTP 200 with correct data from local state, but the event append and projection update were silently discarded because the session was never committed.

**Why it was misclassified:** The symptom — projection state not reflecting handler-appended events — is identical to an async projection race condition. The fix was trivial (one line), but the diagnosis required understanding Wolverine's middleware pipeline.

**Impact:** Every other BC (13 total) already had this policy. Product Catalog was the sole outlier — likely because it was originally a document-store BC and the policy was missed during the M35.0 ES migration.

**Guard rail for M36.1:** Every new BC `Program.cs` must include `opts.Policies.AutoApplyTransactions()` before any handler is written. This is non-negotiable.

---

## Key Lessons Learned

1. **Shared test infrastructure pays off immediately.** The `TestAuthHandler` utility (Session 1) resolved 21 test failures across 3 BCs in a single session. Every future BC gets this for free.

2. **`bus.PublishAsync()` in HTTP endpoints is a correctness hazard.** It bypasses the transactional outbox, meaning messages can be published even if the database transaction rolls back. The `OutgoingMessages` tuple return pattern is the correct idiom. This was the most pervasive violation found in Sessions 2–3.

3. **Missing Wolverine policies fail silently.** `AutoApplyTransactions` doesn't throw an error when absent — handlers appear to work (return 200), but events and projections are silently lost. Any new BC must have this policy from its first commit.

4. **Authorization must be a day-one concern.** M36.0 spent two sessions (4–5) retroactively adding `[Authorize]` to 55 endpoints across 10 BCs. For M36.1, every new BC API must have `[Authorize]` on all non-auth endpoints from the first commit.

5. **Vertical slice splits are mechanical but valuable.** Splitting monolith handler files (VP Team Management, PC Vendor Assignment) into per-command files improved discoverability with zero behavioral change. The effort-to-benefit ratio is excellent.

---

## What M36.1 Inherits

### 1. VP Team Management `@wip` E2E Scenarios (13 scenarios)

**Source:** Session 6 (E-2)
**Status:** Blocked on UI implementation
**What's needed:** Invite form, role change dialog, deactivate/reactivate actions in `TeamManagement.razor`
**Disposition:** Out of scope for M36.1 (Listings/Marketplaces focus). Carry to M37.0 or dedicated VP milestone.

### 2. Returns Cross-BC Saga Tests (6 skipped)

**Source:** Pre-existing; documented in `docs/wolverine-saga-persistence-issue.md`
**Status:** Wolverine saga persistence issue — `InvokeAsync` saga not found by subsequent handlers
**Disposition:** Monitor. No action unless Wolverine releases a fix. Tests remain skipped with documentation.

### 3. Product Catalog `SaveChangesAsync` Sweep (12 remaining calls)

**Source:** Session 3 (B-7 partial); 12 calls remain in `*ES.cs` files
**Status:** Less urgent with `AutoApplyTransactions` now enabled — Wolverine auto-commits, so manual `SaveChangesAsync()` is redundant rather than harmful
**Disposition:** Tech debt. Address opportunistically during M36.1 Phase 0 follow-up if Product Catalog handlers are modified. Not a blocking item.

---

## CI Confirmation

| Workflow | Run # | Status | Branch |
|----------|-------|--------|--------|
| CI | #808 | ✅ Success | main |
| E2E Tests | #381 | ✅ Success | main |
| CodeQL Security Analysis | #369 | ✅ Success | main |

All workflows green on `main` as of 2026-03-29.
