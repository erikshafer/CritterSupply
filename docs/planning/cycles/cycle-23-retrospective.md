# Cycle 23 Retrospective: Vendor Portal E2E Testing

**Dates:** 2026-03-11
**Duration:** 1 day (single-session implementation)
**Status:** ✅ **COMPLETE** — all sign-offs obtained (QA Engineer, Product Owner, Principal Architect)

---

## Objectives

Build Playwright + Reqnroll end-to-end browser tests for the Vendor Portal — covering JWT authentication, real-time SignalR updates, change request workflows, and RBAC enforcement across a 3-server test fixture (VendorIdentity.Api + VendorPortal.Api + Blazor WASM static host).

## Key Deliverables

- ✅ **3-server E2E fixture** — VendorIdentity.Api + VendorPortal.Api + WASM static host running as real Kestrel servers
- ✅ **12 BDD scenarios** across 3 feature files (P0 + P1a coverage)
- ✅ **Page Object Models** — Login, Dashboard, Change Requests, Submit, Settings pages
- ✅ **SignalR hub message injection testing** via `IHubContext` for real-time notification verification
- ✅ **JWT authentication flow** — Real token lifecycle in browser context (not mocked)
- ✅ **Playwright tracing** enabled for CI failure diagnosis

## What Was Completed

### Phase 1 — Test Infrastructure
- Created `VendorPortal.E2ETests` project with Playwright + Reqnroll
- Built 3-server WebApplicationFactory fixture (real Kestrel, not TestServer)
- Configured shared PostgreSQL via TestContainers
- JWT token flow tested end-to-end in browser

### Phase 2 — P0 Scenarios
- Login/logout flow with JWT token persistence
- Dashboard data loading with real API responses
- Basic navigation and page rendering verification

### Phase 3 — P1a Scenarios
- Change request submission and lifecycle (Create → Review → Approve/Reject)
- SignalR real-time notification verification via hub context injection
- RBAC enforcement (vendor vs admin capabilities)

## Lessons Learned

### L1 — WASM Static Host Requires Special Handling
**What happened:** Blazor WASM apps don't use `WebApplicationFactory` the same way as server apps — the WASM binary must be served as static files from a real HTTP server.
**Fix:** Created a dedicated static file server hosting the WASM `wwwroot` output.
**Propagated to:** `docs/skills/e2e-playwright-testing.md`

### L2 — SignalR Hub Testing via IHubContext
**What happened:** Testing real-time updates requires injecting messages into the SignalR hub from the test harness.
**Fix:** Used `IHubContext<VendorPortalHub>` to send messages directly, then asserted DOM updates via Playwright.
**Propagated to:** `docs/skills/e2e-playwright-testing.md`

## Metrics

| Category | Count |
|----------|-------|
| E2E Scenarios | 12 |
| Feature Files | 3 |
| Page Object Models | 5 |
| Servers in Fixture | 3 |
| Duration | 1 day |

## Process Improvements

- **Event modeling before E2E:** The Cycle 22 event modeling and BDD scenarios made E2E test design straightforward — scenarios mapped directly to test cases
- **Page Object Model pattern** continues to pay dividends — encapsulates MudBlazor component interactions cleanly
- **Playwright tracing** should be enabled by default in CI for all E2E test projects

## Next Steps

- **Cycle 24:** Fulfillment Integrity + Returns Prerequisites — fix critical bugs blocking Returns BC
- **Cycle 25:** Returns BC Phase 1 — self-service returns + order history page

## Summary

Cycle 23 completed the Vendor Portal E2E testing initiative, delivering 12 BDD scenarios across a 3-server test fixture. The Playwright + Reqnroll infrastructure established in Cycle 20 (Storefront E2E) proved directly reusable for the Vendor Portal context, validating the investment in shared testing patterns. With Cycles 22-23 complete, the Vendor Portal is the most thoroughly tested bounded context in CritterSupply.

---

*Created: 2026-03-12*
*Cycle Plan: [cycle-23-plan.md](cycle-23-plan.md)*
*E2E Architecture: [cycle-23-vendor-portal-e2e-testing.md](cycle-23-vendor-portal-e2e-testing.md)*
