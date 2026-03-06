# Cycle 20: Automated Browser Testing

**Status:** 📋 Planned (depends on Cycle 19.5 completion)  
**GitHub Milestone:** [Cycle 20](https://github.com/erikshafer/CritterSupply/milestones) *(to be created)*  
**Epic Issue:** *(to be created)*  
**ADR:** [ADR 0015 — Playwright E2E Browser Testing](../../decisions/0015-playwright-e2e-browser-testing.md)  
**Original Issue:** [#58 — Automated browser tests for Customer Experience Blazor UI](https://github.com/erikshafer/CritterSupply/issues/58)

---

## Goal

Establish automated end-to-end (E2E) browser testing for the Customer Experience BC.

The checkout wizard (Checkout.razor), Cart page, and Order Confirmation page are currently verified only through manual testing. Cycle 20 closes three documented test gaps:

| Gap | Current State | After Cycle 20 |
|---|---|---|
| SignalR hub delivery to browser | Only message production is tested | Playwright verifies DOM updates |
| Blazor rendering + MudStepper navigation | Manual only | E2E scenario covers all 4 steps |
| Cookie auth + protected route redirect | Manual only | E2E scenario covers login → checkout flow |

---

## Dependency

**Cycle 19.5 (Complete Checkout Workflow) must complete before Cycle 20 begins.**

Cycle 20 requires a fully interactive checkout wizard — each step must call its respective backend API and the stepper must advance on success. If the checkout is still display-only (Cycle 19.5 in progress), E2E tests cannot exercise the meaningful flows.

---

## Technology Decisions

| Decision | Choice | Rationale |
|---|---|---|
| Browser automation | **Microsoft Playwright (C#)** | Real Kestrel server, auto-wait for Blazor, Trace Viewer for CI debugging |
| Component testing | **Skip bUnit in Cycle 20** | MudStepper's JS interop makes bUnit impractical for the checkout wizard |
| Test driver | **Reqnroll (ADR 0006)** | `checkout-flow.feature` already written; no duplication in two formats |
| Server isolation | **Real Kestrel + stub HTTP clients** | Same pattern as `Storefront.Api.IntegrationTests`; no full docker-compose |
| Database | **TestContainers PostgreSQL** | Real Marten event store; same pattern as all existing integration tests |
| CI trigger | **main push + nightly** | NOT on every PR (E2E runtime: 10–15 min; PR CI must stay fast) |

See [ADR 0015](../../decisions/0015-playwright-e2e-browser-testing.md) for full rationale.

---

## Architecture

```
Playwright (Chromium, headless)
      │
      ▼
Storefront.Web (real Kestrel, random port, in-process)
      │  HTTP
      ▼
Storefront.Api (real Kestrel, random port, in-process)
      ├── IShoppingClient         → StubShoppingClient
      ├── IOrdersClient           → StubOrdersClient
      ├── ICatalogClient          → StubCatalogClient
      └── ICustomerIdentityClient → StubCustomerIdentityClient
            │
            ▼
      TestContainers PostgreSQL (real Marten event store)
```

Both Storefront.Web and Storefront.Api run as real Kestrel TCP servers — required because:
- Playwright's browser needs a real HTTP address (TestServer doesn't bind a TCP port)
- SignalR's WebSocket upgrade requires a real HTTP server

---

## Test Project Structure

```
tests/Customer Experience/Storefront.E2ETests/
├── Storefront.E2ETests.csproj        ← Playwright + Reqnroll + xUnit
├── WellKnownTestData.cs              ← Stable constants (Alice's IDs, addresses, products)
├── ScenarioContextKeys.cs            ← ScenarioContext key constants
├── E2ETestFixture.cs                 ← Fixture: servers + TestContainers + stub clients
├── Hooks/
│   ├── PlaywrightHooks.cs            ← Browser context lifecycle (per scenario)
│   └── DataHooks.cs                  ← Infrastructure lifecycle + data seeding
├── Pages/                            ← Page Object Models
│   ├── LoginPage.cs
│   ├── CartPage.cs
│   ├── CheckoutPage.cs               ← 4-step MudStepper wizard (all data-testid selectors)
│   └── OrderConfirmationPage.cs      ← Order ID, status chip, SignalR notification area
└── Features/
    ├── checkout-flow.feature         ← Gherkin scenarios (Phase 1 + 2)
    └── CheckoutFlowStepDefinitions.cs ← Reqnroll bindings → Page Object Models
```

---

## Scenarios to Implement

### Phase 1: Checkout Wizard (No Real-Time)

| Priority | Scenario | Tags |
|---|---|---|
| **P0** | Complete checkout successfully (happy path, all 4 steps) | `@checkout` |
| **P0** | Cannot proceed to checkout with empty cart | `@checkout` |
| **P1** | Order summary totals update with Express shipping | `@checkout` |
| **P1** | Checkout fails if payment token is invalid | `@checkout` |

### Phase 2: Real-Time SignalR Order Status Updates

| Priority | Scenario | Tags |
|---|---|---|
| **P2** | Order confirmation page receives payment authorized event | `@checkout @signalr` |
| **P2** | Order confirmation page receives shipment dispatched event | `@checkout @signalr` |

### Deferred to Future Cycles

| Scenario | Tag | Reason |
|---|---|---|
| Mobile responsive layout | `@mobile @wip` | Requires viewport configuration; low ROI in Cycle 20 |
| Keyboard accessibility | `@accessibility @wip` | Specialized testing; separate cycle |
| Add new address during checkout | — | Requires Customer Identity BC live integration; stub complexity |
| Multi-tab cart sync | — | Covered by SSE/SignalR integration tests; low E2E ROI |

---

## Prerequisites Before Writing Tests

### 1. `data-testid` Attributes ✅ (Completed in Cycle 20 planning)

Added to `Checkout.razor`:
- `data-testid="checkout-stepper"` — outer MudStepper
- `data-testid="checkout-step-title"` — active step heading
- `data-testid="address-select"` — MudSelect for saved addresses
- `data-testid="shipping-method-standard/express/nextday"` — radio buttons
- `data-testid="payment-token-input"` — payment token text field
- `data-testid="btn-save-address"` / `"btn-save-shipping-method"` / `"btn-save-payment"` / `"btn-place-order"` — action buttons
- `data-testid="order-subtotal"` / `"order-shipping-cost"` / `"order-total"` — summary values

Added to `OrderConfirmation.razor`:
- `data-testid="order-id"` — order ID text
- `data-testid="order-status"` — status MudChip (target for SignalR updates)
- `data-testid="signalr-connected"` — connection indicator
- `data-testid="order-update-notification"` — real-time notification area

### 2. Cycle 19.5 Completion

The checkout stepper must call backend APIs when user progresses through steps.

### 3. Well-Known Test Data Alignment

Alice's customer ID (`11111111-1111-1111-1111-111111111111`) must match the seed data used in Customer Identity integration tests for auth to succeed.

---

## Test Data Strategy

```
[BeforeTestRun]   → Start E2E fixture (1× per run — amortizes 5–10 sec startup cost)
[BeforeScenario]  → Reset stubs + seed standard checkout data (cart + addresses + products)
[AfterScenario]   → Clean Marten database (complete isolation per scenario)
[AfterTestRun]    → Dispose fixture (stop Kestrel servers + TestContainers)
```

**Key principle:** The browser only touches what the test is testing. Everything else (login, cart population, address seeding) is done via API or stub — never via browser UI navigation.

**Exception:** The login step does use the browser (LoginPage.LoginAsync) because cookie-based auth must be established through the real Blazor auth flow to set the session cookie correctly.

---

## Tasks

### Planning & Prerequisites (Cycle 20 start)

- [x] Create ADR 0015 (Playwright E2E browser testing strategy)
- [x] Add `data-testid` attributes to Checkout.razor
- [x] Add `data-testid` attributes to OrderConfirmation.razor
- [x] Update `checkout-flow.feature` (fix SSE → SignalR references)
- [x] Scaffold `Storefront.E2ETests` project (fixture, hooks, page objects, step definitions)
- [x] Add `Microsoft.Playwright` + `Microsoft.AspNetCore.Mvc.Testing` to `Directory.Packages.props`
- [x] Register `Storefront.E2ETests` in `CritterSupply.slnx`
- [x] Create `.github/workflows/e2e.yml` (separate from integration test workflow)

### Implementation (During Cycle 20)

- [ ] Wire up `E2ETestFixture` to start real Kestrel servers (validate startup + port binding)
- [ ] Implement `DataHooks.BeforeTestRun` (validate infrastructure starts cleanly in CI)
- [ ] Verify Playwright browser install in CI (`playwright.ps1 install chromium`)
- [ ] **Phase 1:** Implement P0 scenario — complete checkout happy path
- [ ] **Phase 1:** Implement P0 scenario — empty cart validation
- [ ] **Phase 1:** Implement P1 scenario — Express shipping total update
- [ ] **Phase 1:** Implement P1 scenario — invalid payment token error
- [ ] **Phase 2:** Implement SignalR payment authorized scenario
- [ ] **Phase 2:** Implement SignalR shipment dispatched scenario
- [ ] Validate all Phase 1 tests pass in CI (e2e.yml nightly run)
- [ ] Validate all Phase 2 tests pass in CI

### Documentation

- [ ] Update `CURRENT-CYCLE.md` when Cycle 20 starts
- [ ] Create cycle retrospective at end of Cycle 20
- [ ] Update `CONTEXTS.md` with E2E test layer (if integration contract changes)
- [ ] Close GitHub Issue #58 with reference to this cycle

---

## Risks

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| MudStepper rendering timing causes flaky tests | Medium | High | Use `WaitForLoadStateAsync(NetworkIdle)` after each step; never use `Thread.Sleep` |
| Playwright browser install fails in CI | Low | High | Pin Playwright version; use `--with-deps` flag for system dependency install |
| Real Kestrel factory port binding race condition | Low | Medium | Use `UseUrls("http://127.0.0.1:0")` (OS-assigned port); extract bound address after start |
| Auth cookie not propagated across Blazor Server requests | Medium | High | Test login flow early in Cycle 20; investigate cookie domain/path if issues arise |
| SignalR WebSocket upgrade blocked by test environment | Low | High | Use Kestrel directly (not proxy); disable `HTTPS_REDIRECT` in test configuration |
| TestContainers Docker socket unavailable in CI | Low | High | `ubuntu-latest` has Docker pre-installed; add `DOCKER_HOST` env var explicitly |
| Cycle 19.5 not completed before Cycle 20 starts | Medium | High | Hard dependency; do not start Phase 1 implementation until checkout stepper is live |
| `data-testid` attributes removed during MudBlazor upgrade | Low | Medium | Document `data-testid` convention; add check in code review checklist |

---

## Success Criteria

**Cycle 20 is complete when:**

- [ ] All Phase 1 E2E scenarios pass consistently in `e2e.yml` nightly run
- [ ] All Phase 2 SignalR scenarios pass consistently in `e2e.yml` nightly run
- [ ] `e2e.yml` CI workflow runs without manual intervention
- [ ] No `@wip` scenarios are accidentally included in the CI run
- [ ] Playwright traces are saved as artifacts on CI failures
- [ ] Test execution time is < 15 minutes
- [ ] GitHub Issue #58 closed

---

## References

- [ADR 0015 — Playwright E2E Browser Testing](../../decisions/0015-playwright-e2e-browser-testing.md)
- [ADR 0006 — Reqnroll for BDD Testing](../../decisions/0006-reqnroll-bdd-framework.md)
- [ADR 0013 — SignalR Migration from SSE](../../decisions/0013-signalr-migration-from-sse.md)
- [checkout-flow.feature (docs/features)](../../features/customer-experience/checkout-flow.feature)
- [checkout-flow.feature (E2E tests)](../../../tests/Customer%20Experience/Storefront.E2ETests/Features/checkout-flow.feature)
- [Storefront.E2ETests project](../../../tests/Customer%20Experience/Storefront.E2ETests/)
- [.github/workflows/e2e.yml](../../../.github/workflows/e2e.yml)
- [SignalRNotificationTests.cs — documented gap](../../../tests/Customer%20Experience/Storefront.Api.IntegrationTests/SignalRNotificationTests.cs)
- [Cycle 19.5 manual testing guide](./cycle-19.5-manual-testing-guide.md)
- [GitHub Issue #58](https://github.com/erikshafer/CritterSupply/issues/58)
- [Microsoft Playwright .NET Documentation](https://playwright.dev/dotnet/)
