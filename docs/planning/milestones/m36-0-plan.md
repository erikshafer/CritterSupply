# M36.0 Plan — Engineering Quality

**Status:** 🚀 In progress — Session 1  
**Date:** 2026-03-28  
**Owners:** M36.0 planning panel (architecture, QA, UX, security, event modeling synthesis)

---

## Goal

M36.0 makes what exists more correct, more consistent, more testable, and more aligned with the domain — so that when M36.1+ begins product expansion, the codebase feels like it was written by one disciplined team with a shared vocabulary and shared patterns.

---

## Scope boundary

**M36.0 is NOT:**
- A new bounded context (no Search, Recommendations, Store Credit, Variants, Listings, or Marketplaces)
- A new aggregate, projection, or product capability
- A feature milestone — no user-visible behavior is added

**M36.0 IS:**
- Fix every pre-existing test failure
- Eliminate Critter Stack anti-patterns that violate Wolverine's transactional contract
- Audit and normalize DDD naming where violations exist
- Complete vertical slice colocation per ADR 0039
- Close E2E coverage gaps for already-implemented pages
- Add authorization attributes to unprotected endpoints
- Remove dead-end UI placeholders and vocabulary drift

---

## Planning session findings

The planning panel conducted independent research before convening. Key findings that shape this plan:

### Pre-existing test failures — root cause classified

| BC | Failures | Root Cause | Classification |
|----|----------|-----------|----------------|
| **Orders** | 15 | All return HTTP 401 — test fixture lacks fake JWT auth after `[Authorize]` was added to endpoints | Test bug (infrastructure gap) |
| **Customer Identity** | 4 | Same pattern — GET endpoints now require `[Authorize]`, fixture has no auth bypass | Test bug (infrastructure gap) |
| **Correspondence** | 1 of 2 | GET endpoint requires `[Authorize]`, fixture has no auth bypass | Test bug (infrastructure gap) |
| **Correspondence** | 1 of 2 | `OrderPlaced` handler emits duplicate `CorrespondenceQueued` — handler publishes event AND cascading `SendMessage` also publishes it | Implementation bug (duplicate event emission) |

**20 of 21 failures share one root cause:** test fixtures lack fake JWT authentication. A single shared `TestAuthHandler` utility + fixture updates across 3 BCs resolves all 20. The 21st failure is a genuine implementation bug in Correspondence.

### Critter Stack idiom violations — confirmed

| ID | Severity | BC | File | Violation |
|----|----------|-----|------|-----------|
| CS-1 | 🔴 Critical | Payments | `Processing/AuthorizePayment.cs` | Manual `session.Events.StartStream()` instead of returning `(IStartStream, OutgoingMessages)` tuple — events may bypass transactional middleware |
| CS-2 | 🔴 Critical | Payments | `Processing/PaymentRequested.cs` | Same as CS-1 |
| CS-3 | 🟡 High | Returns | `ReturnProcessing/RequestReturn.cs` | `bus.PublishAsync()` for 4 integration events instead of returning `OutgoingMessages`; only `bus.ScheduleAsync()` is justified |
| CS-4 | 🟡 High | Inventory | `InventoryManagement/AdjustInventory.cs` | `bus.PublishAsync()` for integration events + manual `SaveChangesAsync()` instead of `OutgoingMessages` return + auto-transaction |
| CS-5 | 🟡 High | Orders | `Placement/CancelOrderEndpoint.cs` | `bus.PublishAsync(new CancelOrder(...))` instead of returning cascading command |

**34 manual `SaveChangesAsync()` calls** identified across Vendor Portal (12), Vendor Identity (9), Pricing (5), Product Catalog (2), Backoffice (1), and others — all redundant where Wolverine auto-transaction is configured.

### DDD naming audit — remarkably clean

All 170+ event classes use correct past-tense naming. **Zero** persisted event renames needed. **Zero** migration ADRs required.

**3 naming violations found (all safe to rename — not persisted in event streams):**

| ID | Current Name | Proposed Name | BC | Rationale |
|----|-------------|--------------|-----|-----------|
| N-1 | `PaymentRequested` (command) | `RequestPayment` | Payments | Command named as event; collides with integration message pattern |
| N-2 | `RefundRequested` (internal command) | `RequestRefund` | Payments | Same issue; name-collides with `Messages.Contracts.Payments.RefundRequested` (integration event) |
| N-3 | `CalculateDiscountRequest` | `CalculateDiscount` | Promotions | `Request` suffix is a technical artifact; inconsistent with every other command in the codebase |

**4 command-style integration events** (`FulfillmentRequested`, `ReservationCommitRequested`, `ReservationReleaseRequested`, `RefundRequested`) use `*Requested` suffix as a conscious loose-coupling convention. These are not violations — they should be documented as an explicit convention or converted to Wolverine routed commands in a future milestone.

**1 namespace ownership question:** `Messages.Contracts.Shopping.CartCheckoutCompleted` is published by Orders BC, not Shopping BC. Decision deferred to implementation session — requires team alignment on which BC "owns" the checkout completion boundary.

### Vertical slice violations — 5 files need refactoring

| ID | BC | File | Issue |
|----|-----|------|-------|
| VS-1 | Vendor Portal | `TeamManagement/TeamEventHandlers.cs` | 7 handler classes in 1 file (227 lines) |
| VS-2 | Product Catalog | `Products/AssignProductToVendorES.cs` | 3 handlers + 2 commands + 2 validators in 1 file (324 lines) |
| VS-3 | Returns | `ReturnProcessing/RequestReturn.cs` | Large combined slice (278 lines) — extract response types, fix `bus.PublishAsync` |
| VS-4 | Vendor Identity | `UserInvitations/` | 3 validators in separate files from their commands/handlers |
| VS-5 | Vendor Identity | `UserManagement/` | 3 validators in separate files from their commands/handlers |

### Authorization coverage — significant gaps

**51% of HTTP endpoints lack `[Authorize]` attributes.** Token handling is solid (in-memory JWT, HttpOnly refresh cookies), but 7 of 13 API projects have unprotected endpoints.

| Severity | BC | Gap |
|----------|-----|-----|
| 🔴 Critical | Vendor Identity | No auth middleware in pipeline — tenant CRUD fully exposed |
| 🔴 Critical | Shopping + Storefront | Zero authorization on all cart/checkout flows |
| 🔴 Critical | Returns | 9 unprotected mutation endpoints (approve, deny, ship-replacement) |
| 🔴 High | Fulfillment | 5 unprotected shipment operations |
| 🔴 High | Product Catalog | 12 unprotected write operations |
| 🔴 High | Customer Identity | 7 unprotected account operations |

### E2E coverage map

| App | Active E2E Scenarios | Coverage |
|-----|---------------------|----------|
| Backoffice | 118 across 11 features | 🟢 Strong — gaps: Order Search/Detail |
| Vendor Portal | 12 across 3 features | 🔴 Weak — 6 of 10 page-flows uncovered |
| Storefront | ~7 active (8 @ignore) | 🔴 Critical — product browsing, cart, real-time all uncovered |

### UI quality findings

| Priority | Finding |
|----------|---------|
| P0 | "Coming soon" text in customer-facing Checkout page — ships unfinished-product language |
| P0 | Storefront brand shows "Storefront.Web" instead of "CritterSupply" |
| P1 | Counter and Weather Blazor template pages still shipped in Storefront |
| P1 | VP Dashboard Team Management button disabled ("coming soon") but the page is fully built at `/team` |
| P2 | "Pricing Admin" vs "Product Catalog" — same route, different nav labels by role |
| P2 | Two pages claim "Dashboard" title (`/` and `/dashboard`) |

### Returns RabbitMQ skips — not a TestContainers problem

The 6 skipped tests already use TestContainers for Postgres and RabbitMQ. The blocker is a **Wolverine saga persistence issue** in multi-host test configuration — sagas created via `InvokeAsync()` are not visible to subsequent handlers. Documented in `docs/wolverine-saga-persistence-issue.md`.

**Recommendation:** Split test strategy — (a) test saga creation in Orders integration tests (already passes); (b) test cross-BC routing separately without depending on a pre-existing saga. Monitor Wolverine framework updates.

### Modeling gaps — no feature coverage for core orchestration

The Order Saga (512 lines, 18 handler methods, compensation flows) has **zero Gherkin scenarios**. Payment Processing, Promotions, and Correspondence also lack feature documentation entirely. These are candidates for retroactive modeling, but writing Gherkin features is outside M36.0's scope (no new behavior). Documented here for M36.1+ planning.

---

## Sequenced work items

### Track A: Fix pre-existing test failures (prerequisite for all other tracks)

All other work depends on a green test suite. This track comes first.

**A-1. Create shared `TestAuthHandler` utility**

- **Owner:** QA
- **Acceptance criteria:**
  - A reusable fake `AuthenticationHandler<AuthenticationSchemeOptions>` exists in a shared test utility location
  - The handler auto-authenticates all requests with configurable claims (user ID, roles)
  - Pattern follows established Alba + TestContainers conventions
- **Dependencies:** None

**A-2. Fix Orders test fixture — add fake auth**

- **Owner:** QA
- **Acceptance criteria:**
  - All 15 previously-failing Orders integration tests pass
  - Test fixture bypasses all authorization policies used by Orders.Api endpoints
  - No test is skipped, ignored, or removed
  - Total Orders integration tests: 48/48 passing
- **Dependencies:** A-1

**A-3. Fix Customer Identity test fixture — add fake auth**

- **Owner:** QA
- **Acceptance criteria:**
  - All 4 previously-failing Customer Identity integration tests pass
  - Total Customer Identity integration tests: 29/29 passing
- **Dependencies:** A-1

**A-4. Fix Correspondence test fixture — add fake auth**

- **Owner:** QA
- **Acceptance criteria:**
  - The 1 auth-related Correspondence test failure is fixed
  - Total auth-related fixes: 1/1
- **Dependencies:** A-1

**A-5. Fix Correspondence duplicate event emission**

- **Owner:** PSA
- **Acceptance criteria:**
  - `OrderPlaced` handler chain emits exactly 1 `CorrespondenceQueued` event, not 2
  - The test `OrderPlaced_publishes_CorrespondenceQueued_integration_event` passes
  - No downstream consumers are broken by the fix
  - Total Correspondence integration tests: 5/5 passing
- **Dependencies:** A-4 (fixture must be auth-fixed first to isolate this bug)

**Track A exit criteria:** `dotnet test` across Orders, Customer Identity, and Correspondence produces 0 failures. CI run is green.

---

### Track B: Critter Stack idiom compliance

Fix violations of Wolverine's transactional contract. These are correctness issues — events that bypass the outbox, messages that bypass cascading, persistence that bypasses auto-transaction.

**B-1. Payments — return `(IStartStream, OutgoingMessages)` tuple**

- **Owner:** PSA
- **Acceptance criteria:**
  - `AuthorizePayment.cs` and `PaymentRequested.cs` (internal command handler) return `(IStartStream, OutgoingMessages)` instead of calling `session.Events.StartStream()` manually
  - Events are persisted via Wolverine's transactional middleware, not manual session calls
  - All Payments integration tests pass
- **Dependencies:** Track A complete

**B-2. Returns — replace `bus.PublishAsync()` with `OutgoingMessages` return**

- **Owner:** PSA
- **Acceptance criteria:**
  - `RequestReturn.cs` returns `(Events, OutgoingMessages)` instead of calling `bus.PublishAsync()` for integration events
  - `bus.ScheduleAsync()` (for expiration timer) remains — this is the only justified `IMessageBus` injection
  - All Returns integration tests pass (excluding the 6 cross-BC skips)
- **Dependencies:** Track A complete

**B-3. Inventory — replace `bus.PublishAsync()` and remove manual `SaveChangesAsync()`**

- **Owner:** PSA
- **Acceptance criteria:**
  - `AdjustInventory.cs` returns `OutgoingMessages` instead of calling `bus.PublishAsync()`
  - Manual `SaveChangesAsync()` removed — Wolverine auto-transaction handles persistence
  - All Inventory integration tests pass
- **Dependencies:** Track A complete

**B-4. Orders — replace `bus.PublishAsync()` in `CancelOrderEndpoint`**

- **Owner:** PSA
- **Acceptance criteria:**
  - `CancelOrderEndpoint.cs` returns cascading `CancelOrder` command instead of calling `bus.PublishAsync()`
  - All Orders integration tests pass
- **Dependencies:** A-2 (Orders auth fix)

**B-5. Vendor Portal — remove redundant `SaveChangesAsync()` (12 handlers)**

- **Owner:** PSA
- **Acceptance criteria:**
  - All 12 Vendor Portal handlers that call `SaveChangesAsync()` on `IDocumentSession` have the call removed
  - Wolverine's `IntegrateWithWolverine()` + auto-transaction handles persistence
  - All Vendor Portal integration tests pass
- **Dependencies:** Track A complete

**B-6. Pricing — remove redundant `SaveChangesAsync()` (5 calls)**

- **Owner:** PSA
- **Acceptance criteria:**
  - `SetBasePriceEndpoint.cs`, `SchedulePriceChangeEndpoint.cs`, and `CancelScheduledPriceChangeEndpoint.cs` have manual `SaveChangesAsync()` calls removed
  - All Pricing integration tests pass
- **Dependencies:** Track A complete

**B-7. Product Catalog — remove redundant `SaveChangesAsync()` in `AssignProductToVendorES.cs`**

- **Owner:** PSA
- **Acceptance criteria:**
  - 2 manual `SaveChangesAsync()` calls removed
  - All Product Catalog integration tests pass
- **Dependencies:** Track A complete

**Track B exit criteria:** Zero `bus.PublishAsync()` calls for integration event publishing (except `bus.ScheduleAsync()` for delayed messages). Zero manual `SaveChangesAsync()` in handlers where Wolverine auto-transaction is configured. `dotnet test` green across all affected BCs.

---

### Track C: DDD naming and vertical slice compliance

Safe renames and file restructuring. No persisted event renames — all candidates are non-persisted commands.

**C-1. Rename `PaymentRequested` → `RequestPayment` (internal command)**

- **Owner:** PSA
- **Acceptance criteria:**
  - Command record, handler class, validator (if any), and file name are all renamed
  - Order saga dispatch site updated
  - Integration message `Messages.Contracts.Payments.RefundRequested` (the event) is NOT renamed — it is correctly named
  - All Payments and Orders integration tests pass
- **Dependencies:** B-1 (Payments idiom fix first — avoid double-touching these files)

**C-2. Rename `RefundRequested` → `RequestRefund` (internal command only)**

- **Owner:** PSA
- **Acceptance criteria:**
  - Internal Payments command renamed; eliminates name collision with integration event
  - All Payments integration tests pass
- **Dependencies:** B-1

**C-3. Rename `CalculateDiscountRequest` → `CalculateDiscount`**

- **Owner:** PSA
- **Acceptance criteria:**
  - Command record, validator, handler, and all call sites (Shopping BC `PromotionsClient`) updated
  - All Promotions and Shopping integration tests pass
- **Dependencies:** Track A complete

**C-4. Vertical slice refactoring — Vendor Portal `TeamEventHandlers.cs`**

- **Owner:** PSA
- **Acceptance criteria:**
  - 7 handler classes split into 7 individual files in `TeamManagement/`
  - Each file follows ADR 0039 pattern: handler class + any associated types
  - Manual `SaveChangesAsync()` removed during split (combines with B-5)
  - All Vendor Portal tests pass
- **Dependencies:** B-5 (combine with SaveChangesAsync removal)

**C-5. Vertical slice refactoring — Product Catalog `AssignProductToVendorES.cs`**

- **Owner:** PSA
- **Acceptance criteria:**
  - Split into `GetVendorAssignment.cs`, `AssignProductToVendor.cs`, `BulkAssignProductsToVendor.cs`
  - Each file contains its command + handler + validator + response types
  - All Product Catalog integration tests pass
- **Dependencies:** B-7 (combine with SaveChangesAsync removal)

**C-6. Vertical slice refactoring — Vendor Identity validators**

- **Owner:** PSA
- **Acceptance criteria:**
  - 6 separate validator files (`UserInvitations/` and `UserManagement/`) colocated with their command/handler files per ADR 0039
  - All Vendor Identity integration tests pass
- **Dependencies:** Track A complete

**C-7. Document the `*Requested` integration event convention**

- **Owner:** EMF
- **Acceptance criteria:**
  - ADR created (or section added to existing integration-messaging skill file) documenting that `*Requested` integration messages are a conscious loose-coupling convention for command-intent messages between BCs
  - The 4 specific messages (`FulfillmentRequested`, `ReservationCommitRequested`, `ReservationReleaseRequested`, `RefundRequested`) are cited as canonical examples
  - Decision recorded: keep as convention now; evaluate Wolverine routed commands in a future milestone
- **Dependencies:** None

**Track C exit criteria:** Zero command classes using past-tense naming. Zero bulk handler/validator files violating ADR 0039. Convention documented for command-style integration events.

---

### Track D: Authorization hardening

Add `[Authorize]` attributes to unprotected endpoints. The Returns BC Session 6 fix is the template.

**D-1. Vendor Identity — add auth middleware and policies**

- **Owner:** ASIE
- **Acceptance criteria:**
  - JWT Bearer auth middleware added to Vendor Identity API pipeline
  - All tenant management endpoints (create, suspend, reinstate, terminate) require appropriate authorization
  - All user management endpoints (invite, deactivate, reactivate, change role) require appropriate authorization
  - Auth endpoints (login, logout, refresh) are explicitly `[AllowAnonymous]`
  - All Vendor Identity integration tests updated with fake auth and pass
- **Dependencies:** Track A pattern (shared TestAuthHandler)

**D-2. Shopping + Storefront — add authorization to cart/checkout endpoints**

- **Owner:** ASIE
- **Acceptance criteria:**
  - All Shopping.Api endpoints have `[Authorize]` with appropriate policy
  - All Storefront.Api endpoints have `[Authorize]` with appropriate policy
  - Integration tests updated with fake auth and pass
- **Dependencies:** Track A pattern

**D-3. Returns — add authorization to mutation endpoints**

- **Owner:** ASIE
- **Acceptance criteria:**
  - 9 unprotected mutation endpoints (approve, deny, receive, inspect, ship-replacement, etc.) have `[Authorize]` with appropriate policy
  - Existing 2 protected endpoints unchanged
  - All Returns integration tests pass
- **Dependencies:** Track A pattern

**D-4. Fulfillment, Product Catalog, Customer Identity — add authorization**

- **Owner:** ASIE
- **Acceptance criteria:**
  - Fulfillment: 5 shipment operation endpoints protected
  - Product Catalog: 12 write operation endpoints protected
  - Customer Identity: 7 account/address endpoints protected
  - All integration tests updated and pass
- **Dependencies:** Track A pattern

**D-5. Orders — add authorization to checkout mutation endpoints**

- **Owner:** ASIE
- **Acceptance criteria:**
  - 4 unprotected checkout mutation endpoints have `[Authorize]`
  - All Orders integration tests pass (builds on A-2 fixture fix)
- **Dependencies:** A-2

**Track D exit criteria:** Every HTTP endpoint across all BCs either has `[Authorize]` with an appropriate policy or `[AllowAnonymous]` with documented justification. No endpoint is unintentionally unprotected.

---

### Track E: UI cleanup and VP Team Management E2E

**E-1. Remove dead-end UI placeholders**

- **Owner:** UXE
- **Acceptance criteria:**
  - "Coming soon" text removed from Storefront Checkout page — replaced with neutral copy or removed entirely
  - Storefront brand changed from "Storefront.Web" to "CritterSupply"
  - Counter and Weather Blazor template pages removed from Storefront, NavMenu links removed
  - VP Dashboard Team Management button enabled (page exists at `/team`) or hidden entirely — no more "coming soon" for implemented features
  - All E2E tests pass
- **Dependencies:** None

**E-2. VP Team Management E2E — page object and step definitions**

- **Owner:** QA
- **Acceptance criteria:**
  - `TeamManagementPage.cs` page object created with ~15 methods covering roster display, invitation display, and admin-only gating
  - Step definitions created for the scenarios that match current page implementation (roster viewing, admin gating, pending invitations)
  - Scenarios requiring unimplemented UI actions (invite form, role change buttons, deactivate/reactivate buttons) are tagged `@wip` with a comment explaining the blocker
  - All non-`@wip` scenarios pass in CI
- **Dependencies:** E-1 (VP Dashboard button must be enabled first)

**E-3. Backoffice Order Search/Detail E2E**

- **Owner:** QA
- **Acceptance criteria:**
  - E2E feature file and step definitions created for Order Search and Order Detail pages
  - Page objects created for both pages
  - At least happy-path scenarios pass in CI
- **Dependencies:** Track A (Orders auth fix), Track D (Orders auth hardening)

**Track E exit criteria:** Zero "coming soon" text for implemented features. Zero Blazor template pages in production NavMenu. VP Team Management scenarios executable. Backoffice Order Search/Detail has E2E coverage.

---

## Deferred items

| Item | Reason for deferral |
|------|-------------------|
| **Vendor Identity `SaveChangesAsync()` removal (9 handlers)** | EF Core auto-transaction wiring needs verification first — `UseEntityFrameworkCoreTransactions()` may not be configured. Requires investigation before removal is safe. Deferred to M36.1. |
| **`CartCheckoutCompleted` namespace ownership** | Requires team alignment on whether Shopping or Orders "owns" the checkout completion boundary. Design decision, not a code fix. Deferred to M36.1. |
| **Backoffice `OrderPlacedHandler` read-after-write `SaveChangesAsync()`** | Intentional pattern for inline projection flush + query. Architecturally questionable but functional. Needs design review. Deferred to M36.1. |
| **Command-style integration events → Wolverine routed commands** | Conscious decision to keep `*Requested` convention for now and document it (C-7). Conversion to routed commands is a future architectural evolution. Deferred to M37.0+. |
| **Returns cross-BC test skips (6 tests)** | Blocked by Wolverine saga persistence issue in multi-host tests, not by missing infrastructure. Split test strategy recommended but requires framework-level investigation. Deferred — monitor Wolverine updates. |
| **Retroactive Gherkin features for Order Saga, Payments, Promotions, Correspondence** | Writing behavioral specifications for existing code is valuable but is documentation/modeling work, not engineering quality. Belongs in a modeling-focused milestone. Deferred to M36.1+. |
| **Storefront and Vendor Portal E2E coverage beyond Team Management** | Storefront product browsing, cart, and real-time flows have zero E2E coverage, but writing those tests requires stable, correctly-authorized endpoints first. Tracks A+D must complete before this work is meaningful. Deferred to M36.1. |
| **UI vocabulary alignment (Pricing Admin vs Product Catalog labels, dual Dashboard pages)** | Requires design decisions about information architecture, not just code changes. Deferred to M36.1. |
| **Owner-validation middleware (customer can only access own cart/address)** | Authorization hardening (Track D) adds policy-level auth. Resource-level ownership validation is a deeper security layer that requires per-endpoint logic. Deferred to M36.1. |
| **`RequestReturn.cs` vertical slice split (VS-3)** | The file is large (278 lines) but is a single cohesive slice. The `bus.PublishAsync` fix (B-2) is the priority; splitting the file is polish. Deferred to M36.1. |

---

## Guard rails for M36.0 implementation sessions

These are non-negotiable constraints drawn from the pre-planning findings and M33.0 key learnings.

1. **Classify before fixing.** Every pre-existing failure must be classified (test bug, implementation bug, infrastructure bug) before a fix is proposed. Do not "just fix" something without understanding what category of problem it is.

2. **Do not return tuples from handlers that manually load aggregates.** This was the root cause of a 30-minute debugging incident documented in the handler skill file. Tuple returns only work with `[WriteAggregate]`. If you are calling `session.LoadAsync<T>()` or `session.Events.StartStream()`, you must use explicit session operations or restructure to use aggregate attributes.

3. **Do not remove `SaveChangesAsync()` without verifying auto-transaction configuration.** Wolverine's auto-transaction requires `AutoApplyTransactions()` (or `IntegrateWithWolverine()` for Marten). Before removing a manual save, confirm the BC's `Program.cs` has the correct middleware. EF Core BCs additionally need `UseEntityFrameworkCoreTransactions()`.

4. **Do not rename persisted events without a migration ADR.** All 170+ events in the codebase use correct past-tense naming — no renames are needed. The 3 naming fixes in Track C are all non-persisted commands. If a future session discovers a persisted event that needs renaming, stop and create an ADR first.

5. **Test fixtures must bypass ALL authorization policies, not just one.** The root cause of 20 out of 21 pre-existing failures is a test fixture that lacks auth bypass. When fixing a fixture, enumerate every `[Authorize(Policy = "...")]` used by the BC's endpoints and bypass all of them. A fixture that bypasses `CustomerService` but not `FinanceClerk` will produce intermittent 401s.

6. **Commit frequently — each Track item is one commit.** Do not batch multiple track items into a single commit. If a session completes A-2 and A-3, those are two separate commits. This enables clean revert if any fix introduces a regression.

---

## Definition of done for M36.0

M36.0 is complete when ALL of the following are true:

1. **Zero pre-existing test failures.** `dotnet test` across all BCs produces 0 failures. The 21 failures inherited from M35.0 are all resolved.

2. **Zero Critter Stack idiom violations in priority BCs.** No handler in Orders, Shopping, Payments, Returns, Inventory, Vendor Portal, Pricing, or Product Catalog uses `bus.PublishAsync()` for integration events (except `bus.ScheduleAsync()` for delayed messages). No handler calls `SaveChangesAsync()` where Wolverine auto-transaction is configured.

3. **Zero DDD naming violations.** `PaymentRequested` → `RequestPayment`, `RefundRequested` → `RequestRefund`, `CalculateDiscountRequest` → `CalculateDiscount`. The `*Requested` integration event convention is documented.

4. **Vertical slice compliance.** No bulk handler/validator files violating ADR 0039 remain in Vendor Portal, Product Catalog, or Vendor Identity.

5. **Authorization coverage.** Every HTTP endpoint across all BCs has either `[Authorize(Policy = "...")]` or explicit `[AllowAnonymous]`. No endpoint is unintentionally unprotected.

6. **VP Team Management E2E operational.** At least the roster-viewing and admin-gating scenarios from `team-management.feature` are executable and passing in CI.

7. **Backoffice Order Search/Detail E2E exists.** At least happy-path E2E scenarios for Order Search and Order Detail are passing in CI.

8. **UI placeholders cleaned.** Zero "coming soon" text for implemented features. Zero Blazor template pages in Storefront NavMenu.

9. **CI green.** CI Run, E2E Run, and CodeQL Run all green on main after final merge.

---

## Recommended session sequencing

| Session | Primary Track | Items | Expected Outcome |
|---------|--------------|-------|-----------------|
| **1** | Track A (test failures) | A-1 through A-5 | All 21 pre-existing failures resolved; CI green |
| **2** | Track B (idiom compliance) | B-1 through B-4 (critical + high) | Payments, Returns, Inventory, Orders idiom violations fixed |
| **3** | Track B + C (idiom + naming) | B-5 through B-7, C-1 through C-3 | Remaining SaveChangesAsync removed; 3 command renames done |
| **4** | Track C + D (slices + auth) | C-4 through C-7, D-1 | Vertical slices refactored; Vendor Identity auth hardened |
| **5** | Track D (auth hardening) | D-2 through D-5 | All endpoint authorization gaps closed |
| **6** | Track E (UI + E2E) | E-1 through E-3 | UI cleanup; VP Team Mgmt E2E; Backoffice Order E2E |

Sessions may be combined or resequenced based on velocity, but the dependency ordering must be preserved: A before B/C/D, B-1 before C-1/C-2, A-2 before D-5, E-1 before E-2.
