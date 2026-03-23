# M33.0 Post-Mortem & Recovery Review

**Date:** 2026-03-23  
**Status:** ⚠️ Review complete — remediation required before M33.0 can be considered fully closed

---

## Purpose

This document records the honest, evidence-based review of where M33.0 actually landed versus what was planned, after the Session 5/6 retrospective was found to contain inaccurate claims about bUnit test completion.

This review synthesizes three independent perspectives:
- **PSA (Principal Software Architect)** — backend completeness, architecture, structural correctness
- **UXE (UX Engineer)** — planned vs delivered user-facing behavior
- **QAE (QA Engineer)** — planned vs actual test coverage and release risk

---

## Evidence Reviewed

### Planning + status documents
- `docs/planning/milestones/m33-0-session-3-plan.md`
- `docs/planning/milestones/m33-0-session-3-status.md`
- `docs/planning/milestones/m33-0-session-5-plan.md`
- `docs/planning/milestones/m33-0-session-5-status.md`
- `docs/planning/milestones/m33-0-session-6-status.md`
- `docs/planning/milestones/m33-0-session-5-retrospective.md`
- `docs/planning/CURRENT-CYCLE.md`
- `docs/planning/milestones/m33-m34-engineering-proposal-2026-03-21.md`

### Verifiable git history on `origin/main`
- `79487e1` — M33.0 Session 3: backend/search/filter work + status docs
- `4689b07` — merged Session 5/6 work, including corrected retrospective text

### Important note on commit evidence

The individual Session 5/6 PR-branch commits cited in milestone docs (for example `78fe1b4`, `aa740e9`, `c51c714`) are **not present on `origin/main`** after squash/merge. They are still referenced in the merged commit message for `4689b07` and in the session documents, but they are not directly inspectable from the main branch history anymore.

That matters because part of the retrospective confusion came from relying on secondary summaries instead of reconciling the repo state on `origin/main`.

---

## 1. Honest M33.0 Completion Status

## What was planned

From `m33-0-session-3-plan.md`, M33.0 Priority 3 was supposed to deliver:

1. **Order Search page** at `/orders/search`
   - search by order number, customer email, customer name
   - results table with status
   - single-click navigation to order detail
   - role-gated access
2. **Return Management page** at `/returns`
   - active queue defaulting to pending stage
   - status filtering
   - count badge aligned with dashboard `PendingReturns`
   - single-click navigation to return detail
   - role-gated access
3. **NavMenu updates** for both pages
4. **Backoffice.Web.UnitTests** with initial bUnit coverage
5. **Verification**
   - integration tests
   - passing bUnit tests
   - manual verification

## What actually landed

### Backend that really shipped

Session 3 (`79487e1`) did deliver real backend work:

- `src/Orders/Orders.Api/Placement/SearchOrdersEndpoint.cs`
  - adds `/api/orders/search`
  - but only supports **exact GUID search**
  - customer email/name search was deferred
- `src/Returns/Returns/Returns/ReturnQueries.cs`
  - adds optional `status` filter support to `/api/returns`
- `src/Backoffice/Backoffice/Clients/IOrdersClient.cs`
  - adds `SearchOrdersAsync()`
- `src/Backoffice/Backoffice/Clients/IReturnsClient.cs`
  - adds `status` support to `GetReturnsAsync()`

### Frontend that really shipped

Session 5/6 work, as merged in `4689b07`, added:

- `src/Backoffice/Backoffice.Web/Pages/Orders/OrderSearch.razor`
- `src/Backoffice/Backoffice.Web/Pages/Returns/ReturnManagement.razor`
- `src/Backoffice/Backoffice.Web/Layout/NavMenu.razor` updates
- `tests/Backoffice/Backoffice.Web.UnitTests/` infrastructure only
- corrected retrospective/status docs explaining that the tests were removed

### What was missed or only partially delivered

- **Order Search scope was narrowed** from order number/email/name search to exact GUID search only
- **Return Management** shipped as a page shell, but not with verified dashboard parity
- **Detail navigation was not delivered**
  - both pages currently show disabled “View Details” actions
- **bUnit coverage did not land**
  - the final state is **0 Backoffice.Web tests**, not passing coverage
- **manual verification was not completed**
- **replacement E2E coverage was not added**

## Bottom line

M33.0 Priority 3 was **partially delivered, not fully completed**.

What exists today is:
- backend enablers: **partial**
- frontend page shells: **present**
- working, verified end-to-end customer-service workflow: **not proven**
- UI coverage: **missing**

---

## 2. PSA Findings — Architectural and Technical State

## Backend completed vs planned

### Completed
- Orders search endpoint exists: `src/Orders/Orders.Api/Placement/SearchOrdersEndpoint.cs`
- Returns status filter exists: `src/Returns/Returns/Returns/ReturnQueries.cs`
- Backoffice client contracts were updated in Session 3

### Not completed as planned
- no search by customer email or customer name
- no Backoffice.Api search/list BFF endpoints for the new pages
- no integration tests were added for the new search/filter endpoints

## Current architectural state of the new features

### Order Search

The page exists, but its current wiring is not trustworthy:

- `OrderSearch.razor` calls `/api/orders/search`
- the page uses the named client `"BackofficeApi"` from `src/Backoffice/Backoffice.Web/Program.cs`
- Backoffice.Api exposes `/api/backoffice/...` routes such as:
  - `src/Backoffice/Backoffice.Api/Queries/GetOrderDetailView.cs`

There is **no matching `/api/orders/search` endpoint in Backoffice.Api**.

That means the current page is calling a route shape that does not match the API host it is configured to use.

### Return Management

The page exists, but has the same service-boundary problem:

- `ReturnManagement.razor` calls `/api/returns`
- the page also uses the `"BackofficeApi"` client
- Backoffice.Api only exposes `/api/backoffice/...` routes for returns detail, not a list endpoint

There is **no matching `/api/returns` endpoint in Backoffice.Api**.

### Status vocabulary drift

The Return Management page defaults to `"Pending"`:
- `src/Backoffice/Backoffice.Web/Pages/Returns/ReturnManagement.razor`

But the Returns BC enum uses values like:
- `Requested`
- `Approved`
- `Denied`
- `InTransit`
- `Received`
- `Completed`
- `Expired`

See:
- `src/Returns/Returns/Returns/ReturnQueries.cs`
- `src/Returns/Returns/Returns/ReturnStatus.cs`

That means the default filter label is not aligned with the domain vocabulary, and invalid filter values silently fall back to an unfiltered list.

## Structural/correctness concerns from Sessions 5/6

1. **Wrong service boundary in Backoffice.Web**
   - the app points at Backoffice.Api but calls domain API routes directly
2. **NavMenu visibility does not match page authorization**
   - pages allow `customer-service,operations-manager,system-admin`
   - nav links are inside `AuthorizeView Policy="CustomerService"`
   - `operations-manager` can have route access but not link discoverability
3. **Status terminology is inconsistent**
   - `PendingReturns` dashboard tile is based on `ReturnMetricsView.ActiveReturnCount`
   - the page default/filter language says `Pending`
4. **PR-branch history is no longer directly visible on main**
   - retrospective claims are easier to get wrong when only merged summaries are consulted

## Honest technical debt picture

M33.0 leaves behind technical debt in four categories:

1. **BFF route mismatch**
2. **authorization/discoverability inconsistency**
3. **status vocabulary mismatch**
4. **missing automated coverage for the new user flows**

---

## 3. UXE Findings — User-Facing State

## Planned frontend scope

Planned UI scope from the session plan/proposal:
- Order Search page with meaningful search inputs and actionable results
- Return Management page with a pending-focused queue and dashboard parity
- visible nav links for the intended roles
- usable end-to-end customer-service workflow

## What users actually got

### Delivered
- visible pages:
  - `src/Backoffice/Backoffice.Web/Pages/Orders/OrderSearch.razor`
  - `src/Backoffice/Backoffice.Web/Pages/Returns/ReturnManagement.razor`
- new nav items:
  - `src/Backoffice/Backoffice.Web/Layout/NavMenu.razor`

### Not actually delivered
- detail navigation
- broader search by order number/email/name
- verified queue behavior matching the dashboard metric
- replacement E2E coverage proving the flow works

## Current user-facing state

### Order Search
- **Exists**
- **Partial UI shell**
- copy explicitly says “Search orders by order ID (GUID format)”
- results show short IDs rather than more customer-service-friendly identifiers
- “View Details” is disabled
- because of the current route mismatch, the core search flow is not reliable

### Return Management
- **Exists**
- **Partial UI shell**
- filter defaults to `Pending`, which does not align with the underlying Returns status enum
- “View Details” is disabled
- count badge is local page state, not demonstrated dashboard parity
- because of the current route mismatch, the core list/filter flow is not reliable

### NavMenu
- links are present
- discoverability is inconsistent for `operations-manager`
- footer text still says `Session 7-8: Add real pages`, which conflicts with the pages already being linked

## UX conclusion

From a user perspective, M33.0 delivered **visible scaffolding**, not a fully trustworthy customer-service workflow.

---

## 4. QAE Findings — Test History and Coverage

## Original test plan

The plan called for:
- new Backoffice.Web bUnit project
- page tests for Order Search and Return Management
- NavMenu role-gating tests
- optional dashboard KPI tests
- existing integration tests still green
- manual verification

## What tests were written, then removed

Per the Session 5/6 documentation and merged `4689b07` commit message:

- bUnit tests were created during Session 5
- those tests later hit authorization-related issues
- Session 6 removed all Backoffice.Web test files
- the final repository state keeps only test infrastructure:
  - `tests/Backoffice/Backoffice.Web.UnitTests/Backoffice.Web.UnitTests.csproj`
  - `tests/Backoffice/Backoffice.Web.UnitTests/BunitTestBase.cs`
  - `tests/Backoffice/Backoffice.Web.UnitTests/TestHelpers.cs`

The final state is **0 Backoffice.Web tests**.

## Was removing bUnit acceptable?

### Tactical answer
Removing the specific bUnit tests was understandable once they ran into:
- policy-based authorization
- component auth wrappers
- SignalR-connected page initialization

### QA answer
Removing **all** UI coverage with **no replacement coverage** is **not** an acceptable finished state.

The problem is not “bUnit failed.”  
The problem is “coverage was removed and no equivalent E2E or page-level coverage replaced it.”

## Coverage gaps created by M33.0

1. no automated coverage for Order Search page behavior
2. no automated coverage for Return Management page behavior
3. no automated coverage for the new NavMenu links and role visibility
4. no integration tests for `/api/orders/search`
5. no integration tests for `/api/returns?status=...`
6. no completed manual verification evidence

## Risk if we move into M34.0 without addressing these

### High risk

Why:
- the pages are auth-heavy and hub-connected
- the route mismatch means the most important flows may already be broken
- current test suites do not exercise these new flows
- the milestone docs would continue overstating what is actually safe to build on

---

## 5. Unified Risk Assessment

If we move forward without recovery work, the main risks are:

1. **Broken customer-service flows**
   - pages exist in navigation but may fail at runtime
2. **False confidence from milestone reporting**
   - “complete” status does not match repo reality
3. **Regression risk**
   - there is no automated safety net for these pages
4. **Incorrect operational behavior**
   - returns filter semantics and dashboard parity are not aligned
5. **Wasted follow-on effort in M34.0**
   - future work may build on assumptions that are currently false

---

## 6. Remediation Plan

## Must address immediately (before calling M33.0 closed / before using these pages as completed foundation)

### 1. Fix the Backoffice BFF route mismatch
- add Backoffice.Api endpoints for the new page workflows, or
- intentionally reconfigure the web app to call the correct downstream API host/routes

Recommended shape:
- `/api/backoffice/orders/search`
- `/api/backoffice/returns`

### 2. Align role visibility with page access
- make NavMenu visibility match page authorization
- remove the current `operations-manager` discoverability mismatch

### 3. Correct return status vocabulary
- decide whether Backoffice should expose `Requested` directly, or map it to `Pending`
- make the UI, BFF, and dashboard metric language consistent

## Should be incorporated into the next execution session / early M34.0 recovery scope

### 4. Add replacement automated coverage
- E2E tests for:
  - Order Search
  - Return Management
  - NavMenu visibility by role
  - session-expired handling
- targeted API integration tests for:
  - `/api/orders/search`
  - `/api/returns?status=...`

### 5. Complete or intentionally remove the dead-end detail actions
- either implement:
  - order detail navigation
  - return detail navigation
- or remove/replace the disabled “View Details” buttons until the workflow exists

## Track as explicit debt if not done immediately

### 6. Restore the fuller Order Search experience
- search by customer email
- search by customer name
- human-friendly order-number search if distinct from GUID

### 7. Reconcile milestone/status documentation
- keep the corrected retrospective
- link this post-mortem anywhere Priority 3 is referenced as “complete”

---

## 7. Process Observation

The session gaps and retrospective error happened because three things compounded:

1. **Session continuity broke**
   - Session 3 stopped after backend work
   - Session 4 never happened
   - later sessions resumed from incomplete state
2. **Secondary summaries were trusted over repo reality**
   - the incorrect “13/13 bUnit tests passing” claim made it into documentation even though the final repo state had zero tests
3. **Squash-merge history obscured intermediate branch commits**
   - after merge, only the summarized `4689b07` remained on main
   - that made it easier to reason from write-ups instead of verifying the current tree

## Prevention

For future milestone recovery work:

- verify completion claims against `origin/main`, not just session summaries
- treat “tests deferred” as unresolved until replacement coverage exists
- do not mark milestone scope complete while critical user flows remain unverified
- when a session stops mid-plan, record a short “what is actually merged vs still missing” checkpoint before the next session begins

---

## Final Recommendation

Treat M33.0 Priority 3 as **partially delivered and in recovery**, not as a cleanly completed milestone item.

The next execution session should focus first on:
1. fixing the BFF route mismatch
2. aligning authorization/discoverability
3. correcting return queue/status semantics
4. adding replacement automated coverage

Only after those are done should this work be treated as a stable base for later milestones.
