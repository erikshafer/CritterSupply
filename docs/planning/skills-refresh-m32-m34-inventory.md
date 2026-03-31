# Skills Refresh Inventory — M32.x through M34.0

**Created:** 2026-03-31
**Scope:** Retrospectives from M32.0, M32.1, M32.2, M32.3, M32.4, M33.0, and M34.0 milestones
**Purpose:** Decision record for which skill files need updates based on gap-fill analysis against lessons already captured in the M35.0–M36.1 refresh (PR #500)
**Companion:** [`skills-refresh-m35-m36-inventory.md`](./skills-refresh-m35-m36-inventory.md) — prior refresh inventory

---

## Sources Reviewed

### Milestone Closure Retrospectives (Primary)
- `m32-0-retrospective.md` — Backoffice Phase 1 (BFF foundation, SignalR, projections)
- `m32.1-retrospective.md` — Backoffice Phase 2 (WASM, E2E infrastructure, JWT)
- `m32.2-e2e-retrospective.md` — E2E hardening (session expiry, RBAC, MudBlazor)
- `m32-3-retrospective.md` — Write operations depth (product/pricing/warehouse admin)
- `m32-4-session-1-retrospective.md` — E2E stabilization
- `m33-0-milestone-closure-retrospective.md` — Consolidated quality + structural conformance
- `m34-0-session-1-retrospective.md` — E2E stabilization and RBAC

### Secondary — Saga-Specific Search
Searched all session retrospectives for: `saga`, `OrderDecider`, `OutOfStock`, `MarkCompleted`, `ReturnWindow`, `CompensatedReservation`, `ConcurrencyException`. M32–M34 produced no saga-specific changes (saga refinement was in earlier milestones); the sagas overhaul was driven by verifying the skill file against the **current** implementation.

---

## Inventory by Skill File

### 1. `wolverine-message-handlers.md`
- **Action:** UPDATE
- **Gap source:** M33.0 Cross-Cutting Learning 1
- **Finding:** Mixing `IMessageBus.InvokeAsync()` with manual `session.Events.Append()` in the same endpoint creates two competing persistence strategies. The INV-3 fix in M33.0 Session 1 restored `InvokeAsync()` as the single pattern. This anti-pattern was not yet documented in the skill.
- **What was added:** Anti-Pattern #12 — "Mixing `IMessageBus.InvokeAsync()` with Manual `session.Events.Append()`"

### 2. `critterstack-testing-patterns.md`
- **Action:** UPDATE
- **Gap source:** M33.0 Cross-Cutting Learning 2
- **Finding:** Wolverine's `AutoApplyTransactions()` only fires for HTTP endpoints and Wolverine message handlers — not for direct handler method calls in tests. Tests calling handlers directly must explicitly call `session.SaveChangesAsync()`. This was proven by M33.0 Session 13 (3 tests failed after removing `SaveChangesAsync` from the production handler).
- **What was added:** Section "Auto-Transactions Do Not Apply to Direct Handler Calls" in Unit Testing Pure Functions

### 3. `vertical-slice-organization.md`
- **Action:** UPDATE
- **Gap source:** M33.0 Cross-Cutting Learnings 5 and 6
- **Findings:**
  - L7: Shared value objects (e.g., `RefundAmount`) used across 3+ vertical slices belong in a `/Shared/` folder within the BC, not duplicated per slice.
  - L8: ADR 0039 canonical validator placement convention — top-level `AbstractValidator<T>` colocated in the same file as command + handler. Cross-referenced with ADR 0039.
- **What was added:** Lessons L7 and L8

### 4. `testcontainers-integration-tests.md`
- **Action:** UPDATE
- **Gap source:** M34.0 Session 1 Learning 1
- **Finding:** Configuration values captured eagerly in `Program.cs` (before DI is built) will not reflect `WebApplicationFactory`'s `ConfigureAppConfiguration` overrides. TestContainers fixtures inject dynamic connection strings via this override, but if the connection string is already captured in a local variable, the test connects to the wrong database.
- **What was added:** Pitfall "Eager Configuration Capture in `Program.cs`" in Common Pitfalls section

### 5. `blazor-wasm-jwt.md`
- **Action:** UPDATE
- **Gap source:** M32.3 Session 1 (Local DTO Pattern)
- **Finding:** Blazor WASM projects cannot reference backend projects directly (`Microsoft.NET.Sdk.BlazorWebAssembly` cannot add `<ProjectReference>` to `Microsoft.NET.Sdk.Web`). All types passed across the HTTP boundary must be redefined as local DTO records in the WASM project. This constraint was established in Vendor Portal (M22) and applied to all 10 Backoffice.Web pages in M32.3.
- **What was added:** Section "Local DTOs: No Backend Project References" under GOTCHA Collection: Named HTTP Clients

### 6. `bunit-component-testing.md`
- **Action:** UPDATE
- **Gap source:** M34.0 Session 1 Learnings 3 and 5
- **Findings:**
  - Gotcha #3 expanded: `MudPopoverProvider` requirement also applies to E2E — root `App.razor` must include `<MudPopoverProvider />`. M34.0 found this missing in VendorPortal.Web/App.razor, causing `blazor-error-ui` overlay on pages with `MudSelect`.
  - Gotcha #6 (new): `MudSelect` with `Required="true"` and a programmatic default value is not considered "validated" by `MudForm`'s `@bind-IsValid`. Submit button stays permanently disabled. Fix: remove `Required` from fields with sensible defaults.
- **What was added:** Enhanced gotcha #3 note + new gotcha #6

### 7. `wolverine-sagas.md` ⭐ Major Overhaul
- **Action:** OVERHAUL
- **Gap source:** Implementation drift (the Order saga evolved significantly across M28–M34, but the skill file had not been updated)
- **Findings verified against actual implementation (`src/Orders/Orders/Placement/Order.cs`, `OrderDecider.cs`):**
  - Return processing with `ActiveReturnIds` + `ReturnWindowFired` coordination — 5 return handlers
  - `ReturnWindowExpired` handler no longer unconditionally closes — guards for active returns
  - `PaymentId` tracking on `OrderDecision` and `Order` saga
  - `PaymentAuthorized` handler for two-phase payment flow
  - `DeliveredAt` field for BFF display
  - 19 total handler methods on the saga (was ~12 documented)
- **What was changed:**
  - Added Table of Contents with 18 entries and anchor links
  - Added DOs (15 rules) / DO NOTs (10 rules) section
  - Added "Return Processing — Active Return Tracking" section with coordination logic, closure conditions, and immutable update pattern
  - Updated terminal paths from 4 to 6
  - Updated `ReturnWindowExpired` handler example (active return guard)
  - Fixed `OrderDecision` record to include `PaymentId`
  - Fixed `Handle(PaymentCaptured)` adapter to apply `PaymentId`
  - Updated file organization listing (added value objects and response DTO)
  - Added Pitfall 7 (unconditional close in ReturnWindowExpired)
  - Added Handler Method Summary table (19 handlers with return types and key behaviors)
  - Reorganized Quick Reference checklist into 4 grouped categories

---

## Lower-Priority Files — Assessed as ALREADY CAPTURED

These lessons from M32.x, M33.0, and M34.0 were assessed and found to be **already present** in the skill files (either from the original skill creation or from the M35.0–M36.1 refresh):

| Gap Candidate | Skill File | Status |
|---------------|-----------|--------|
| M32.0 L2 — Inline Projections Require Explicit SaveChanges | `event-sourcing-projections.md` | Already present (lines 521-547) |
| M32.0 L5 — Role-Based vs User-Specific SignalR Groups | `wolverine-signalr.md` | Already present (lines 396-522) |
| M32.1 L1 — Tiered WASM E2E Timeout Strategy | `e2e-playwright-testing.md` | Already present (lines 673-766) |
| M32.1 L3 — Page Object Model Written Before Component | `e2e-playwright-testing.md` | Already present (lines 2075-2136) |
| M32.1 L4/L5 — Auth State Propagation + SignalR JWT Dependency | `e2e-playwright-testing.md` | Covered by tiered timeout table |
| M32.1 W3 — Test-ID Naming Conventions | `e2e-playwright-testing.md` | Already present (lines 321-370) |
| M33.0 L3 — bUnit Policy-Based Authorization Limitations | `bunit-component-testing.md` | Already present (line 350) |
| M33.0 L4 — WaitForURLAsync Instead of WaitForNavigationAsync | `e2e-playwright-testing.md` | Already present (lines 857-869) |

---

## Deliberately Excluded Lessons

| Retro Finding | Why Excluded from Skills |
|---------------|------------------------|
| M32.0 L1 — Pre-Wired Configuration Accelerates Implementation | Planning methodology, not a codeable pattern |
| M32.0 L3 — Stub Clients Require Separate List vs Detail Storage | Test fixture design specific to Backoffice stubs; not generalizable |
| M32.0 L4 — Multi-BC Composition Tests Are End-to-End Workflows | Testing philosophy already captured in critterstack-testing-patterns.md |
| M34.0 L2 — Compare Working Fixtures to Find Differences | Debugging methodology, not a pattern or anti-pattern |
| M34.0 L4 — Pre-existing Failures May Be Hidden by Other Failures | CI troubleshooting methodology |
| M32.3 Session-specific Blazor component patterns | Implementation details, not reusable skill patterns |

---

## Final Results

**Completed:** 2026-03-31

### Files Updated (7 total)

| Skill File | What Was Added |
|------------|---------------|
| `wolverine-message-handlers.md` | Anti-Pattern #12: Mixed InvokeAsync + Events.Append |
| `critterstack-testing-patterns.md` | Auto-transaction direct handler call caveat |
| `vertical-slice-organization.md` | L7: Shared types /Shared/ folder; L8: ADR 0039 validator placement |
| `testcontainers-integration-tests.md` | Eager vs lazy config in WebApplicationFactory pitfall |
| `blazor-wasm-jwt.md` | Local DTO constraint for WASM projects |
| `bunit-component-testing.md` | MudPopoverProvider E2E note; MudForm default value gotcha |
| `wolverine-sagas.md` | Major overhaul: ToC, return processing, DOs/DO NOTs, impl verification |

### Files Reviewed — No Change Required

All lessons from M32.x, M33.0, and M34.0 that were already captured in existing skill files (8 findings) were verified as present and accurate. See "ALREADY CAPTURED" table above.

### Coverage Summary

- **M35.0–M36.1 refresh (PR #500):** 10 skill files updated, 13 no-change
- **M32.x–M34.0 refresh (this PR):** 7 skill files updated (1 major overhaul), 8 findings already captured, 6 findings deliberately excluded
- **Combined coverage:** All retrospectives from M32.0 through M36.1 have been mined and assessed
