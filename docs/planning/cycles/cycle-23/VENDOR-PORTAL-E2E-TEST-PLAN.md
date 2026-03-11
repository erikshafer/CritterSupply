# Cycle 23: Vendor Portal E2E Test Plan

> **Author:** QA Engineer
> **Date:** June 2025
> **Status:** Planning — Ready for team review
> **Depends On:** 143 existing VendorPortal.Api + VendorIdentity.Api integration tests

---

## Table of Contents

1. [Test Strategy Summary](#1-test-strategy-summary)
2. [Feature File Inventory & Scenario Count](#2-feature-file-inventory--scenario-count)
3. [Priority-Ordered Implementation Plan](#3-priority-ordered-implementation-plan)
4. [Test Data Strategy](#4-test-data-strategy)
5. [MudSelect Dropdown Mitigation](#5-mudselect-dropdown-mitigation)
6. [Edge Cases & Gaps the Architecture Review Missed](#6-edge-cases--gaps-the-architecture-review-missed)
7. [Logout Testing Recommendation](#7-logout-testing-recommendation)
8. [Infrastructure & Fixture Design](#8-infrastructure--fixture-design)
9. [Page Object Model Inventory](#9-page-object-model-inventory)
10. [Risk Register](#10-risk-register)

---

## 1. Test Strategy Summary

### What E2E Tests Add Over the 143 Integration Tests

The existing integration tests (Alba + TestContainers) cover **all API business logic**. E2E tests cover what integration tests structurally cannot:

| Concern | Integration Tests | E2E Tests |
|---------|:-----------------:|:---------:|
| Handler business logic | ✅ | — |
| HTTP status codes & response shapes | ✅ | — |
| Event persistence to Marten | ✅ | — |
| Wolverine message publishing | ✅ | — |
| **JWT auth through real browser** | — | ✅ |
| **Blazor WASM hydration & rendering** | — | ✅ |
| **SignalR WebSocket in browser** | — | ✅ |
| **Navigation & route protection** | — | ✅ |
| **Role-based UI visibility** | — | ✅ |
| **MudBlazor component rendering** | — | ✅ |
| **Multi-step user journeys** | — | ✅ |
| **data-testid DOM presence** | — | ✅ |

### Guiding Principle: "Few but Potent"

**Target: ~30 scenarios** organized into 6 feature files. Each scenario tests a complete user journey, not a UI component. We seed data via API calls, then use the browser to verify the rendered result and user interactions.

### Testing Pyramid Allocation for Vendor Portal

```
┌──────────────┐
│  ~6 E2E      │  ← SignalR, login flow, protected routes (browser-only)
├──────────────┤
│  ~24 E2E     │  ← User journeys: lifecycle, list, RBAC, settings
├──────────────┤
│  143 integr. │  ← All business logic (existing, complete)
├──────────────┤
│  Unit tests  │  ← Value objects, pure handlers (existing)
└──────────────┘
```

---

## 2. Feature File Inventory & Scenario Count

| Feature File | Focus | Scenarios | P0 | P1 | P2 |
|---|---|:-:|:-:|:-:|:-:|
| [`vendor-portal-e2e-auth.feature`](vendor-portal-e2e-auth.feature) | Login, protected routes, logout | 8 | 3 | 3 | 2 |
| [`vendor-portal-e2e-dashboard.feature`](vendor-portal-e2e-dashboard.feature) | KPI cards, quick actions | 5 | 1 | 3 | 1 |
| [`vendor-portal-e2e-signalr.feature`](vendor-portal-e2e-signalr.feature) | Real-time updates (4 message types) | 7 | 3 | 3 | 1 |
| [`vendor-portal-e2e-change-request-lifecycle.feature`](vendor-portal-e2e-change-request-lifecycle.feature) | Create, submit, view, withdraw, respond | 9 | 2 | 3 | 4 |
| [`vendor-portal-e2e-change-request-list.feature`](vendor-portal-e2e-change-request-list.feature) | List, filter, navigate, delete | 8 | 0 | 6 | 2 |
| [`vendor-portal-e2e-rbac.feature`](vendor-portal-e2e-rbac.feature) | Role-based visibility & permissions | 7 | 0 | 5 | 2 |
| [`vendor-portal-e2e-settings.feature`](vendor-portal-e2e-settings.feature) | Preferences, saved views | 5 | 0 | 0 | 5 |
| **TOTAL** | | **49** | **9** | **23** | **17** |

---

## 3. Priority-Ordered Implementation Plan

### If we can only implement a subset, implement in this order:

#### Wave 1: P0 — Gate Scenarios (9 scenarios, ~3 days)

These **must pass** before any other E2E work proceeds. They prove the infrastructure works.

| # | Scenario | Why P0 |
|---|----------|--------|
| 1 | Admin logs in and redirects to dashboard | Gate: proves auth + Blazor WASM rendering |
| 2 | Invalid credentials shows inline error | Gate: proves error handling path |
| 3 | Unauthenticated access redirects to login | Gate: proves route protection |
| 4 | Admin sees accurate KPI cards | Gate: proves API→UI data flow |
| 5 | SignalR indicator shows Live | Gate: proves WebSocket connection |
| 6 | Low stock alert updates KPI card | Gate: proves SignalR→UI reactivity |
| 7 | Sales metric banner appears | Gate: proves banner rendering |
| 8 | Submit change request end-to-end | Gate: proves form→API→redirect journey |
| 9 | Save draft change request | Gate: proves minimal form submission |

**Minimum viable E2E suite = Wave 1.** If the cycle is short, ship this.

#### Wave 2: P1 — Core Journeys (23 scenarios, ~5 days)

| Area | Scenarios | Notes |
|------|:---------:|-------|
| Auth (returnUrl, already-auth) | 3 | Low risk, quick to implement |
| Dashboard (quick actions, ReadOnly) | 3 | Role visibility validation |
| SignalR (status update, personal notifications) | 3 | High-value browser-only coverage |
| Change request (detail, withdraw, NeedsMoreInfo) | 3 | Core lifecycle actions |
| List & filter | 6 | Table rendering, chip filtering |
| RBAC | 5 | Security-relevant UI enforcement |

#### Wave 3: P2 — Polish & Edge Cases (17 scenarios, ~4 days)

| Area | Scenarios | Notes |
|------|:---------:|-------|
| Auth (logout, post-logout redirect) | 2 | Nice-to-have, manual testing covers |
| Dashboard (settings navigation) | 1 | Trivial navigation |
| SignalR (disconnected banner) | 1 | Hard to reliably trigger in E2E |
| Change request (delete draft, rejected, superseded, DataCorrection type) | 4 | Lower-risk terminal states |
| List (delete from list, empty state) | 2 | Integration tests cover the API |
| RBAC (ReadOnly settings, role badge) | 2 | Low risk |
| Settings (all 5) | 5 | Well-covered by integration tests |

---

## 4. Test Data Strategy

### Approach: API-Driven Seeding Before Browser Steps

Following the established Storefront E2E pattern, we **seed data via API calls** using test JWTs, then navigate the browser to verify rendered state.

### Pre-Seeded Accounts (No Setup Required)

VendorIdentity.Api seeds these automatically on startup:

| Account | Email | Password | Role | User ID | Tenant ID |
|---------|-------|----------|------|---------|-----------|
| Alice Admin | `admin@acmepets.test` | `password` | Admin | `00000000-...-000000000010` | `00000000-...-000000000001` |
| Bob Catalog | `catalog@acmepets.test` | `password` | CatalogManager | `00000000-...-000000000011` | `00000000-...-000000000001` |
| Carol ReadOnly | `readonly@acmepets.test` | `password` | ReadOnly | `00000000-...-000000000012` | `00000000-...-000000000001` |

### Dynamic Test Data Seeding

For scenarios requiring pre-existing change requests, dashboard data, or saved views:

```
[BeforeScenario, @change-request or @list or @dashboard]
1. Login as admin@acmepets.test → get JWT
2. POST /api/vendor-portal/change-requests/draft (create requests in various states)
3. For non-Draft states: POST .../submit, then inject Catalog BC response via Wolverine
4. For NeedsMoreInfo: Inject MoreInfoRequestedForChangeRequest message
5. For dashboard KPIs: Seed via analytics handlers (LowStockDetected, VendorProductAssociated)
6. Store created IDs in ScenarioContext for browser navigation
```

### WellKnownVendorTestData.cs (Proposed)

```csharp
public static class WellKnownVendorTestData
{
    // Tenant (matches VendorIdentitySeedData)
    public static readonly Guid AcmeTenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    public const string AcmeTenantName = "Acme Pet Supplies";

    // Users (matches VendorIdentitySeedData)
    public static readonly Guid AdminUserId = Guid.Parse("00000000-0000-0000-0000-000000000010");
    public const string AdminEmail = "admin@acmepets.test";

    public static readonly Guid CatalogManagerUserId = Guid.Parse("00000000-0000-0000-0000-000000000011");
    public const string CatalogManagerEmail = "catalog@acmepets.test";

    public static readonly Guid ReadOnlyUserId = Guid.Parse("00000000-0000-0000-0000-000000000012");
    public const string ReadOnlyEmail = "readonly@acmepets.test";

    public const string DefaultPassword = "password";

    // Test SKUs (for change request scenarios)
    public const string DogBowlSku = "DOG-BOWL-01";
    public const string CatToySku = "CAT-TOY-05";
    public const string DogLeashSku = "DOG-LEASH-03";
    public const string CatFoodSku = "CAT-FOOD-02";
    public const string DogBedSku = "DOG-BED-06";
    public const string DogTreatSku = "DOG-TREAT-04";
    public const string DogBoneSku = "DOG-BONE-07";
}
```

### Database Cleanup Strategy

```
[AfterScenario]
1. Delete all ChangeRequest documents (Marten)
2. Delete all VendorAccount saved views (Marten)
3. Reset notification preferences to defaults (Marten)
4. Do NOT delete VendorIdentity seed data (EF Core — shared across scenarios)
```

---

## 5. MudSelect Dropdown Mitigation

### The Problem

The Storefront E2E suite has **5 scenarios marked @ignore** because MudBlazor's `MudSelect` dropdown component doesn't open reliably in headless Playwright:

- MudBlazor renders a transparent `.mud-input-mask` overlay above `.mud-select-input`
- Playwright's `elementFromPoint()` actionability check hits the mask, not the input
- `Force=true` click sometimes opens the popover, but `[role='listbox']` options are rendered in a portal at `<body>` level, making selection unreliable
- Blazor WASM (Vendor Portal) vs Blazor Server (Storefront) may behave differently

### Where MudSelect Appears in Vendor Portal

| Page | Field | data-testid | Default Value | Risk Level |
|------|-------|-------------|---------------|:----------:|
| SubmitChangeRequest.razor | Change Request Type | `type-select` | "Description" | 🟡 Medium |

### Mitigation Strategy

1. **Happy path doesn't need MudSelect**: The default Type is `"Description"` — the most common scenario. The P0 smoke test submits with the default, avoiding the dropdown entirely.

2. **Tag at-risk scenarios**: The single scenario requiring a different Type selection is tagged `@mudselect-risk`:
   ```gherkin
   @change-request @p2 @mudselect-risk
   Scenario: Vendor submits a DataCorrection type change request
   ```

3. **Try the Blazor WASM path first**: MudSelect behavior in WASM may differ from Blazor Server (where the Storefront failed). WASM runs entirely client-side — the JS interop timing may be more predictable. We should attempt:
   ```csharp
   // Attempt 1: Standard click (may work in WASM)
   await TypeSelect.ClickAsync();
   await page.Locator("[role='listbox']").WaitForAsync();
   await page.Locator("[role='option']").Filter(new() { HasText = "Data Correction" }).ClickAsync();

   // Attempt 2: Force click if standard fails
   await TypeSelect.Locator(".mud-select-input").ClickAsync(new() { Force = true });

   // Attempt 3: JavaScript-driven selection
   await page.EvaluateAsync(@"() => {
       const select = document.querySelector('[data-testid=""type-select""]');
       // Dispatch change event with new value
   }");
   ```

4. **Fallback**: If all attempts fail, mark `@mudselect-risk` scenarios as `@ignore` and document in the test report. **The Type field is already covered by integration tests** — the E2E scenario adds only the UI journey value for non-default types.

5. **Track as a known issue**: Create a GitHub issue linking to the Storefront MudSelect bug and this cycle's findings.

---

## 6. Edge Cases & Gaps the Architecture Review Missed

### Gaps Identified

| # | Gap | Severity | Recommendation |
|---|-----|:--------:|----------------|
| 1 | **Blazor WASM hydration timing** | 🔴 High | The Storefront learned the hard way that Blazor components need JS interop initialization time. WASM has its own hydration pattern (.NET runtime download + assembly loading). We need a `WaitForWasmReady()` helper that waits for the .NET runtime to initialize before interacting with MudBlazor components. |
| 2 | **Form validation edge cases** | 🟡 Medium | The submit form requires SKU for "Save as Draft" but all fields for "Submit Request". The architect's journey map doesn't test submitting with missing required fields. Add: attempt submit with empty title → verify validation error. |
| 3 | **Concurrent browser tabs / multi-tab SignalR** | 🟡 Medium | The hub-connection feature file mentions multi-tab deduplication, but this is extremely hard to test in E2E. Defer to manual testing checklist. |
| 4 | **Token refresh during long sessions** | 🟡 Medium | AccessToken expires in 15 minutes. If an E2E test suite takes >15 minutes, mid-suite token refresh could affect later scenarios. The fixture should handle this, but we should add a scenario verifying the token refresh indicator if feasible. |
| 5 | **Network-interrupted SignalR reconnection** | 🟡 Medium | The disconnected banner scenario (P2) requires simulating a network interruption. This is hard to do reliably. Consider Playwright's `page.route()` to block the WebSocket endpoint, or use `context.setOffline(true)`. |
| 6 | **Snackbar timing** | 🟢 Low | MudBlazor snackbars auto-dismiss after a timeout. E2E assertions on snackbar text must use `WaitForSelector` with a reasonable timeout, not a static delay. |
| 7 | **Empty dashboard for brand-new vendor** | 🟢 Low | The architect assumes pre-seeded data. A new vendor with zero SKUs, zero requests, and zero alerts should see informative empty states, not broken cards. Add a P2 scenario. |

### Additional Scenarios Recommended (Not in Architect's Map)

```gherkin
@change-request @p2 @edge-case
Scenario: Submit button is disabled when required fields are empty
  Given I am logged in as "admin@acmepets.test" with password "password"
  When I navigate to the submit change request page
  Then the "Submit Request" button should be disabled
  When I enter "DOG-BOWL-01" in the SKU field
  Then the "Save as Draft" button should be enabled
  But the "Submit Request" button should still be disabled
  When I enter "Title" in the Title field
  And I enter "Details" in the Details field
  Then the "Submit Request" button should be enabled
```

```gherkin
@dashboard @p2 @edge-case
Scenario: New vendor with no data sees informative empty dashboard
  Given I am logged in as a vendor with no products, no requests, and no alerts
  When I am on the dashboard page
  Then the "Low Stock Alerts" KPI card should show "0"
  And the "Pending Change Requests" KPI card should show "0"
  And the "Total SKUs" KPI card should show "0"
```

---

## 7. Logout Testing Recommendation

### Verdict: **YES, test logout E2E — but at P2 priority.**

### Reasoning

| Aspect | Integration Test Coverage | E2E Adds |
|--------|:------------------------:|:--------:|
| POST /auth/logout clears cookie | ✅ Covered | — |
| AuthState.ClearAuthentication() | ✅ Covered | — |
| **SignalR disconnect on logout** | ❌ Not covered | ✅ |
| **Navigation to /login after logout** | ❌ Not covered | ✅ |
| **App bar clears user info** | ❌ Not covered | ✅ |
| **Post-logout route protection** | ❌ Not covered | ✅ |

Integration tests verify the API endpoint works. But the **user-visible logout experience** — SignalR hub disconnects, UI clears, navigation happens, and subsequent route protection works — is only testable in the browser. That said, this is lower risk than login (if login works, logout is a simpler flow), so P2 is appropriate.

The two logout scenarios in `vendor-portal-e2e-auth.feature` cover:
1. Logout → redirect to login + no user info visible
2. Post-logout → protected route redirects to login

---

## 8. Infrastructure & Fixture Design

### Recommended Architecture (Mirrors Storefront Pattern)

```
VendorPortalE2ETestFixture : IAsyncLifetime
├── TestContainers PostgreSQL (shared instance)
├── VendorIdentityApiKestrelFactory
│   ├── EF Core migrations (auto)
│   ├── Seed data (auto — VendorIdentitySeedData)
│   ├── JWT signing key (dev-only)
│   └── Random port binding
├── VendorPortalApiKestrelFactory
│   ├── Marten document store
│   ├── Wolverine messaging (external transports disabled)
│   ├── SignalR hub (/hub/vendor-portal)
│   ├── JWT validation (same signing key)
│   └── Random port binding
├── VendorPortalWebKestrelFactory (Blazor WASM host)
│   ├── Pointed at test VendorIdentity.Api URL
│   ├── Pointed at test VendorPortal.Api URL
│   └── Random port binding
└── Playwright Browser (Chromium, headless)
```

### Key Differences from Storefront Fixture

| Concern | Storefront | Vendor Portal |
|---------|-----------|---------------|
| Web framework | Blazor Server | Blazor WASM (standalone) |
| Auth mechanism | Cookie (session) | JWT + HttpOnly refresh cookie |
| SignalR auth | Cookie-based | JWT from query string |
| API services | 1 BFF (Storefront.Api) | 2 APIs (Identity + Portal) |
| Data seeding | Stub clients (in-memory) | Real API calls + Wolverine handlers |
| External BCs | Stubbed (Shopping, Catalog, etc.) | Stubbed (Catalog BC responses only) |

### Login Helper

The Storefront injects `cartId` into localStorage after login. For Vendor Portal, login produces a JWT that the WASM app stores in-memory (`VendorAuthState`). The browser flow is:

1. Navigate to `/login`
2. Fill email + password
3. Click Sign In
4. WASM calls VendorIdentity.Api → gets JWT
5. Stores token in `VendorAuthState` (memory)
6. Redirects to dashboard
7. Dashboard connects SignalR with JWT from `AccessTokenProvider`

**No localStorage injection needed** — the login flow is clean and testable as-is.

---

## 9. Page Object Model Inventory

### Proposed Page Objects

| Class | Page | Key Locators |
|-------|------|-------------|
| `VendorLoginPage` | `/login` | `GetByLabel("Email")`, `GetByLabel("Password")`, `GetByRole(Button, "Sign In")`, `GetByRole(Alert)` |
| `VendorDashboardPage` | `/dashboard` | `[data-testid='hub-disconnected-banner']`, `[data-testid='sales-metric-updated-banner']`, `[data-testid='submit-change-request-btn']`, KPI card locators |
| `ChangeRequestsListPage` | `/change-requests` | `[data-testid='change-requests-table']`, `[data-testid='submit-change-request-btn']`, `[data-testid='no-requests-message']`, status chips, `[data-testid^='view-request-']`, `[data-testid^='delete-draft-']` |
| `SubmitChangeRequestPage` | `/change-requests/submit` | `[data-testid='sku-field']`, `[data-testid='type-select']`, `[data-testid='title-field']`, `[data-testid='details-field']`, `[data-testid='save-draft-btn']`, `[data-testid='submit-btn']` |
| `ChangeRequestDetailPage` | `/change-requests/{id}` | `[data-testid='withdraw-btn']`, `[data-testid='submit-draft-btn']`, `[data-testid='delete-draft-btn']`, `[data-testid='needs-more-info-alert']`, `[data-testid='provide-info-btn']`, `[data-testid='rejection-reason-alert']`, `[data-testid='superseded-alert']`, `[data-testid='info-responses-thread']` |
| `VendorSettingsPage` | `/settings` | `[data-testid='pref-low-stock-alerts']` through `pref-sales-metrics`, `[data-testid='save-preferences-btn']`, `[data-testid='saved-views-table']`, `[data-testid^='delete-view-']` |
| `VendorAppBar` (shared) | All pages | `GetByRole(Button, "Sign Out")`, role badge chip, user name text, hub status indicator |

### Blazor WASM Wait Strategy

```csharp
// CRITICAL: Blazor WASM needs time to download .NET runtime + assemblies
// before any MudBlazor component is interactive.
public static async Task WaitForWasmReadyAsync(IPage page, int timeoutMs = 30_000)
{
    // Wait for the Blazor WASM runtime to fully load
    await page.WaitForFunctionAsync(
        "() => window.Blazor !== undefined",
        null,
        new() { Timeout = timeoutMs });

    // Wait for MudBlazor JS interop initialization
    await page.WaitForFunctionAsync(
        "() => document.querySelector('.mud-layout') !== null",
        null,
        new() { Timeout = 10_000 });

    // Brief delay for event handler registration
    await Task.Delay(500);
}
```

---

## 10. Risk Register

| Risk | Impact | Likelihood | Mitigation |
|------|:------:|:----------:|------------|
| MudSelect dropdown fails in WASM + Playwright | Medium | Medium | Default value avoids it for P0; @mudselect-risk tag for P2; integration tests cover the API |
| Blazor WASM cold start timeout in CI | High | Medium | Generous timeouts (30s) for first navigation; warm-up request in fixture setup |
| SignalR WebSocket rejected in headless Chrome | High | Low | Storefront proves this works; same Playwright + SignalR pattern |
| Token expiry during long test runs | Medium | Low | 15-min access token; suite should complete in <15 min; add token refresh in fixture if needed |
| Port conflicts with parallel test runs | High | Low | Random port binding (port 0) + sequential execution (maxParallelThreads=1) |
| TestContainers Docker availability in CI | Medium | Low | Docker-in-Docker or service container in GitHub Actions; proven pattern from Storefront |
| Flaky snackbar assertions | Low | Medium | Use `WaitForSelector` with timeout, not `IsVisible` snapshot; assert text content, not animation state |

---

## Appendix: Feature File Cross-Reference

Each E2E feature file maps back to the existing BDD specification files:

| E2E Feature File | Source BDD Spec |
|---|---|
| `vendor-portal-e2e-auth.feature` | `docs/features/vendor-portal/vendor-hub-connection.feature` (auth sections) |
| `vendor-portal-e2e-dashboard.feature` | `docs/features/vendor-portal/vendor-analytics-dashboard.feature` |
| `vendor-portal-e2e-signalr.feature` | `docs/features/vendor-portal/vendor-hub-connection.feature` + `vendor-analytics-dashboard.feature` |
| `vendor-portal-e2e-change-request-lifecycle.feature` | `docs/features/vendor-portal/vendor-change-requests.feature` |
| `vendor-portal-e2e-change-request-list.feature` | `docs/features/vendor-portal/vendor-change-requests.feature` (history section) |
| `vendor-portal-e2e-rbac.feature` | `docs/features/vendor-portal/vendor-change-requests.feature` (RBAC scenarios) |
| `vendor-portal-e2e-settings.feature` | `docs/features/vendor-portal/vendor-analytics-dashboard.feature` (saved views) |

---

## Decision Log

| Decision | Rationale |
|----------|-----------|
| 49 scenarios (not 15-20) | "Few but potent" means fewer than the 143 integration tests, not minimal. 49 scenarios cover all user journeys without redundancy. Implementable in 3 waves. |
| API seeding, not UI seeding | Matches Storefront pattern. Faster, more reliable, decouples setup from browser interaction. |
| No Background login on auth scenarios | Protected route tests require unauthenticated state — Background would force logout. |
| Separate feature files by concern | Allows selective `--filter "Category=signalr"` execution during development. |
| Settings at P2 | 100% covered by integration tests; E2E adds minimal value beyond verifying toggle rendering. |
| Logout at P2, not excluded | SignalR disconnect + navigation is browser-only behavior worth verifying. |
| No multi-tab scenarios | Extremely hard to make reliable in CI; deferred to manual testing checklist. |
