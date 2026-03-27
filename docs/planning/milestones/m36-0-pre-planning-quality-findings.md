# M36.0 Pre-Planning Quality Findings

**Date:** 2026-03-27
**Input From:** M35.0 closure session quality audit
**Purpose:** Ranked list of quality work candidates for M36.0

---

## Context

The M35.0 closure session completed all feature work. The owner's direction for M36.0 is explicit:

> No new BCs for a while. M36.0 is about engineering quality — Critter Stack idioms, DDD-influenced naming practices across all classes/commands/queries/events/messages, and thorough integration and E2E testing everywhere it is missing.

This document presents quality findings ranked by impact to guide M36.0 planning.

---

## Must Fix Before Building Anything More

These are correctness issues and broken flows that erode confidence in the test suite.

### 1. Pre-existing Integration Test Failures (21 failures across 3 BCs)

| BC | Failures | Impact |
|----|----------|--------|
| Orders.Api.IntegrationTests | 15/48 failing | High — core commerce path |
| CustomerIdentity.Api.IntegrationTests | 4/29 failing | Medium — identity management |
| Correspondence.Api.IntegrationTests | 2/5 failing | Low — email templates |

**Action:** Root-cause each failure set. Orders failures are the highest priority given the BC's central role in the saga orchestration model. These failures may mask regressions introduced during the M33.0–M35.0 arc.

### 2. Vendor Portal Team Management E2E Tests

17 Gherkin scenarios exist in `docs/features/vendor-portal/team-management.feature` with no step definitions or page objects. The Blazor page is complete but untested at the E2E level.

**Action:** Create `TeamManagementPage.cs` page object model, implement step definitions in `VendorPortal.E2ETests`, bind to all 17 scenarios.

### 3. Returns Integration Test Skips

6 tests skipped in Returns.Api.IntegrationTests. Skipped tests create blind spots — they either need to be fixed or explicitly removed with a documented reason.

**Action:** Investigate root cause; either fix or delete with explanation.

---

## High Impact, Relatively Contained

These are targeted improvements that deliver significant quality value within bounded scope.

### 4. AssignProductToVendor Document Store Cleanup

The `Product` document model in `ProductCatalog/Products/Product.cs` is no longer the primary persistence mechanism (all handlers now use event sourcing). However, the `Product` class and its `AssignToVendor()`, `Update()`, `ChangeStatus()`, `SoftDelete()` methods still exist in the codebase. The `SeedData.cs` in tests may still reference it.

**Action:** Audit all remaining references to the `Product` document model. If no handler writes to it, remove it or mark it as legacy/migration-only.

### 5. Critter Stack Idiom Compliance Audit

Scan all BCs for:
- Handlers that use `IMessageBus.InvokeAsync()` where event appending would be more idiomatic
- Handlers that manually call `SaveChangesAsync()` where Wolverine's auto-transaction should handle it
- Inconsistent use of tuple returns vs `OutgoingMessages` for integration event cascading
- Handlers in API projects that should be in domain projects (violation of separation of concerns)

**Priority BCs to audit:** Orders, Shopping, Payments (oldest code, most likely to have pre-M33 patterns).

### 6. Event and Command Naming Audit

Scan all events, commands, queries, and messages across all BCs:
- Events must be past tense (`OrderPlaced`, not `PlaceOrder`)
- Commands must be imperative (`PlaceOrder`, not `OrderPlacement`)
- Queries should be named after what they return (`GetCartView`, not `FetchCart`)
- Messages in `src/Shared/Messages.Contracts/` must match their BC's domain language

**Priority:** Focus on contracts in `Messages.Contracts/` since these cross BC boundaries and naming inconsistencies propagate.

### 7. Vertical Slice Completeness

Per ADR 0039, commands, handlers, validators, and events should be colocated in one file per vertical slice. Audit all BCs for:
- Separate files for commands, handlers, and validators that should be colocated
- Missing validators on commands that accept user input
- Events defined far from the handlers that emit them

**Priority BCs:** Older BCs (Orders, Shopping, Payments, Fulfillment) that predate ADR 0039.

---

## Meaningful but Deferrable

These are broader improvements that would be nice but don't block product quality.

### 8. E2E Coverage Gaps Across BCs

| BC | E2E Status | Gap |
|----|-----------|-----|
| Backoffice | Partial (CustomerSearch + CustomerDetail) | Missing: OrderManagement, InventoryAlerts, OperationsAlerts full flows |
| Vendor Portal | Partial (Dashboard, ChangeRequests, Settings) | Missing: TeamManagement (17 scenarios written, no step defs) |
| Storefront | Partial | Missing: checkout flow, order confirmation, real-time updates |

**Action:** Prioritize based on user impact. Vendor Portal Team Management is highest (scenarios already written).

### 9. Test Quality Audit

Review existing test suites for:
- Tests that would pass even if the feature they cover were removed (false positives)
- Tests written against aspirational architecture rather than actual implementation (see M35.0 Session 2 lesson on stale POM locators)
- Integration tests that only cover happy paths, leaving failure modes untested

### 10. UI Vocabulary Consistency

Across Backoffice and Vendor Portal:
- Does the UI language match the domain model events? (e.g., does the UI say "Deactivate" when the event is `VendorUserDeactivated`?)
- Are there placeholder pages or "coming soon" dead ends?
- Is the navigation consistent with user mental models?

### 11. Authorization Coverage

Audit all endpoints across all BCs:
- Are there endpoints that should be protected but are not?
- Are authorization policies named correctly relative to their actual role requirements?
- Is the multi-issuer JWT model consistent across all APIs?

### 12. Modeling Gaps

Identify behaviors in the codebase that were never formally event-modeled:
- Handlers that were written quickly during implementation without a modeling session
- Events or commands that were named quickly and now feel wrong given domain evolution
- Missing events that represent significant state transitions but were never captured

---

## Recommended M36.0 Priority Sequence

1. **Fix pre-existing test failures** (#1) — must be first; restores trust in the suite
2. **VP Team Management E2E tests** (#2) — 17 scenarios ready, page complete
3. **Returns test skip investigation** (#3) — quick triage
4. **Critter Stack idiom audit** (#5) — systematic, high-value cleanup
5. **Event/command naming audit** (#6) — cross-BC impact
6. **Vertical slice completeness** (#7) — pattern consistency
7. **Product document model cleanup** (#4) — contained scope
8. **Broader E2E coverage** (#8) — ongoing investment
9. **Authorization and UI audits** (#10, #11) — polish
10. **Modeling gaps** (#12) — strategic, supports future milestones
