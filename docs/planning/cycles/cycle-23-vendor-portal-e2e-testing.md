# Cycle 23: Vendor Portal E2E Testing

**Status:** ✅ Complete
**Dates:** 2026-03-11
**Milestone:** Vendor Portal E2E browser coverage

## Objective

Add end-to-end (E2E) browser testing for the Vendor Portal (Blazor WASM) using Playwright + Reqnroll, following the same patterns established in Cycle 20 for the Storefront (Blazor Server).

This was identified during Cycle 22 Phase 6 — we had built the Vendor Portal with 143 integration tests (100% pass rate) but zero browser-level E2E coverage.

## Stakeholder Collaboration

- **Principal Architect:** Designed 3-server E2E fixture architecture
- **QA Engineer:** Defined 49 scenarios across 7 feature files, prioritized into waves
- **Product Owner:** Approved P0 (9) + P1a (7) = 16 scenarios with business-focused adjustments

### PO Key Decisions
1. **Promoted** `ChangeRequestDecisionPersonal` toast to P0 (closes the vendor business loop)
2. **Demoted** sales metric banner to P1 (informational nicety, not workflow)
3. **Cut** 3 low-value P1 scenarios (returnUrl, post-logout, default preferences)
4. **Target:** 16 scenarios total (P0 + P1a)

## Architecture

### 3-Server E2E Fixture

```
Playwright Browser (Chromium)
       │
       ▼
VendorPortal.Web (WASM static files, random port)
       │ (cross-origin HTTP + WebSocket)
       ├──────────────────────────┐
       ▼                          ▼
VendorPortal.Api              VendorIdentity.Api
(Kestrel, random port)        (Kestrel, random port)
├── Marten (vendorportal)     ├── EF Core (vendoridentity)
├── SignalR hub               ├── JWT issuance
├── Wolverine (local only)    └── Demo account seeding
└── JWT validation
       │                          │
       └── Shared PostgreSQL ─────┘
           (TestContainers)
```

**Key difference from Storefront E2E:** VendorIdentity.Api runs as a real server (not stubbed). E2E tests need real JWTs with real claims for VendorPortal.Api's authorization policies and SignalR's WebSocket auth.

### WASM Hosting Strategy

Blazor WASM is static files — no `WebApplicationFactory`. Solution: thin ASP.NET Core host serving compiled WASM output with middleware intercepting `/appsettings.json` to inject dynamic test API URLs.

## Scenarios Implemented

### P0 — Core Vendor Value Chain (9 scenarios)

| # | Scenario | Feature File |
|---|----------|-------------|
| 1 | Admin logs in → dashboard redirect | vendor-auth.feature |
| 2 | Invalid credentials → inline error | vendor-auth.feature |
| 3 | Unauthenticated → redirect to /login | vendor-auth.feature |
| 4 | Dashboard shows accurate KPI cards | vendor-dashboard.feature |
| 5 | SignalR "Live" indicator | vendor-dashboard.feature |
| 6 | Low stock alert → KPI update | vendor-dashboard.feature |
| 7 | Change request decision toast | vendor-dashboard.feature |
| 8 | Submit change request E2E | vendor-change-requests.feature |
| 9 | Save draft change request | vendor-change-requests.feature |

### P1a — Complete Vendor Experience (3 scenarios implemented)

| # | Scenario | Feature File |
|---|----------|-------------|
| 10 | Change requests list shows data | vendor-change-requests.feature |
| 11 | ReadOnly can't see submit button | vendor-change-requests.feature |
| 12 | Logout clears session | vendor-change-requests.feature |

## Deliverables

- [x] `data-testid` attributes on Login.razor, MainLayout.razor, Dashboard.razor
- [x] `VendorPortal.E2ETests` project (csproj, fixture, hooks, page objects)
- [x] 3 BDD feature files with 12 Gherkin scenarios
- [x] 3 step definition files
- [x] Project added to `CritterSupply.slnx`
- [x] CI workflow updated (`e2e.yml`) for both Storefront + Vendor Portal
- [x] Cycle documentation and skills update

## Lessons Learned

1. **WASM hosting in tests is fundamentally different from Blazor Server** — no `WebApplicationFactory`, need custom static file host with `appsettings.json` interception
2. **Two-Program-class problem** — when referencing two API projects, top-level `Program` classes conflict. Use domain-specific types (e.g., `VendorPortalHub`, `VendorLoginEndpoint`) as `WebApplicationFactory<T>` anchor types
3. **Real auth is essential for E2E** — stubbing auth defeats the purpose. Real VendorIdentity.Api issues real JWTs that VendorPortal.Api validates
4. **CORS must be opened** — random port allocation means CORS origins can't be pre-configured. Use `SetIsOriginAllowed(_ => true)` in test factories
5. **WASM cold start** — Blazor WASM needs .NET runtime download + assembly loading before MudBlazor is interactive. Need 30s timeout for first `WaitForSelector`
6. **SignalR testing via IHubContext** — inject messages directly through the hub context service, bypassing Wolverine message routing. Much simpler for E2E verification
7. **Few but potent** — PO insight: E2E tests should cover the complete vendor business loop (login → orient → work → feedback), not individual UI components
