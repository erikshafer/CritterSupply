# ADR 0015: Playwright for End-to-End Browser Testing

**Status:** ✅ Accepted

**Date:** 2026-03-06

**Related Issues:** [#58 — Automated browser tests for Customer Experience Blazor UI](https://github.com/erikshafer/CritterSupply/issues/58)

**Companion Cycle:** Cycle 20 — Automated Browser Testing

---

## Context

Cycle 20 is dedicated to establishing automated browser testing for the Customer Experience BC — specifically the Checkout wizard (Checkout.razor), Cart page (Cart.razor), and Order Confirmation page (OrderConfirmation.razor) in Storefront.Web.

Cycle 19.5 completed the wiring of the checkout stepper to real backend APIs. Cycle 20 follows with automated E2E verification of the user-facing flow.

**What the existing test pyramid already covers:**

| Layer | Tool | Status |
|---|---|---|
| Unit tests (pure handlers) | xUnit | ✅ Done |
| API integration tests (all BCs) | Alba + TestContainers | ✅ Done |
| BDD integration tests (auth flow) | Reqnroll + Alba | ✅ Done |
| SignalR message production | Wolverine `InvokeMessageAndWaitAsync` | ✅ Done |
| **SignalR hub delivery to browser** | _(none)_ | ❌ **Gap** |
| **Blazor component rendering** | _(none)_ | ❌ **Gap** |
| **Multi-step checkout wizard navigation** | _(none)_ | ❌ **Gap** |

The explicit gap noted in `SignalRNotificationTests.cs`:
> _"Actual SignalR hub delivery requires full Kestrel (not TestServer) — tested via E2E."_

Cycle 20 closes these three gaps.

**Requirements for the chosen framework:**
- Tests the real browser rendering real Blazor Server components
- Works with real ASP.NET Core Kestrel (not TestServer — required for SignalR WebSocket handshake)
- Integrates with existing xUnit + Reqnroll infrastructure (ADR 0006)
- Runs in CI/CD (GitHub Actions, Linux, headless)
- Page Object Model support for maintainability
- Can wait for async Blazor `StateHasChanged()` renders

---

## Decision

**Use Microsoft Playwright (C#) for end-to-end browser tests. Drive scenarios with Reqnroll step definitions. Deploy in a separate `e2e.yml` GitHub Actions workflow.**

Do **not** use bUnit for the Checkout wizard in Cycle 20.

---

## Rationale

### Why Playwright over bUnit

bUnit is a component unit testing framework designed for isolated Blazor component tests with a simulated render environment. It is the right tool for testing components that:
- Accept simple parameters and render predictable HTML
- Have minimal service injection
- Do not depend on JS interop

`Checkout.razor` disqualifies bUnit on all three criteria:

```
Checkout.razor injects:
  IHttpClientFactory         → painful to stub in bUnit
  NavigationManager          → bUnit supports this but...
  AuthenticationStateProvider → requires fake auth state configuration
  IJSRuntime                 → MudBlazor uses heavy JS interop (MudStepper)
  ISnackbar                  → MudBlazor snackbar service
```

MudBlazor's `MudStepper` component is one of the most JS-coupled in the library. Reliable bUnit tests for a 4-step Linear MudStepper with API calls between steps would require weeks of framework-level mocking — and would not catch the bugs that actually matter: auth redirect failures, MudStepper not advancing, and navigation to `/order-confirmation/{id}`.

**The bugs Playwright catches that bUnit cannot:**
1. Cookie-based auth redirect not working
2. `MudStepper` failing to advance after an async API call
3. Navigation to `/order-confirmation/{orderId}` failing
4. SignalR connection not establishing on the confirmation page
5. Race conditions between `StateHasChanged()` and DOM assertions

**Why Playwright specifically:**
- **Real Kestrel** — Playwright talks to a real ASP.NET Core server over real HTTP. This is the only way to test SignalR WebSocket handshakes from the browser.
- **Auto-wait** — Playwright automatically waits for Blazor's async renders and network idle states, reducing flaky `Thread.Sleep()` workarounds.
- **Trace Viewer** — Built-in trace recording for CI failures (`--trace on`). Invaluable for debugging failures in headless CI.
- **`data-testid` pattern** — Stable selectors decoupled from MudBlazor's internal CSS class structure.
- **Cross-browser** — Chromium for CI, Firefox/WebKit optionally for additional coverage.
- **Mature .NET SDK** — `Microsoft.Playwright` NuGet package, async/await native, integrates with xUnit lifecycle.

### Why Reqnroll Drives the Tests

ADR 0006 adopted Reqnroll as the BDD framework. `checkout-flow.feature` is already written and approved. Implementing Playwright tests as standalone xUnit `[Fact]` methods would duplicate the specification in a second format. Instead:

- Reqnroll `[Binding]` step definitions call Page Object Model methods
- The Gherkin scenarios remain the single source of truth for behavior
- Non-technical stakeholders can read/validate the test specifications
- Living documentation stays current as tests pass/fail

### Test Isolation Architecture (Principal Architect Guidance)

The E2E test fixture avoids running all 7+ services. Instead:

```
Playwright Browser
      │
      ▼
Storefront.Web (real Kestrel, random port, in-process)
      │  (HTTP)
      ▼
Storefront.Api (real Kestrel, random port, in-process)
      │
      ├── IShoppingClient         → StubShoppingClient
      ├── IOrdersClient           → StubOrdersClient
      ├── ICatalogClient          → StubCatalogClient
      └── ICustomerIdentityClient → StubCustomerIdentityClient
            │
            ▼
      TestContainers PostgreSQL (real Marten event store)
```

This matches the already-proven isolation pattern in `Storefront.Api.IntegrationTests`. The stub clients are already written and maintained. TestContainers PostgreSQL gives real Marten event sourcing behavior without requiring a running postgres instance.

**Critical:** Both services use real Kestrel (not TestServer). `WebApplicationFactory` defaults to TestServer, which does not bind to a TCP port — Playwright's browser cannot connect to it. The E2E fixture overrides this to bind to a random localhost port.

### Phase Approach Within Cycle 20

**Phase 1 (Checkout Flow — no real-time):**
- Complete checkout (happy path): 5 steps from cart → order confirmation
- Empty cart validation (Proceed to Checkout disabled)
- Step navigation and back-navigation

**Phase 2 (SignalR order status updates):**
- Order confirmation page subscribes to SignalR
- Inject `OrderStatusChanged` message directly via Wolverine's `InvokeMessageAndWaitAsync`
- Playwright waits for DOM update and asserts new status text

Phase 2 closes the documented `SignalRNotificationTests.cs` gap.

---

## Consequences

### Positive

✅ **Closes the E2E testing gap** — browser, Blazor rendering, SignalR delivery all covered  
✅ **No bUnit overhead** — no fake rendering environment, no IHttpClientFactory mocking  
✅ **Real Kestrel** — SignalR WebSocket upgrade works correctly  
✅ **Reqnroll integration** — `checkout-flow.feature` drives real tests, no duplication  
✅ **Trace Viewer** — CI failures are debuggable without a real browser  
✅ **Page Object Models** — decoupled from MudBlazor internals via `data-testid`  
✅ **Headless CI** — Chromium headless, no X server needed on `ubuntu-latest`  

### Negative

⚠️ **Separate CI pipeline** — E2E tests run on `main` push + nightly, NOT on every PR  
⚠️ **Playwright browser installation** — `playwright install chromium --with-deps` adds ~200MB to CI runner  
⚠️ **Data-testid prerequisites** — MudBlazor components need `data-testid` attributes before E2E tests work  
⚠️ **Timing sensitivity** — Blazor Server async renders require careful use of `WaitForLoadStateAsync` and `WaitForSelectorAsync`  
⚠️ **Two real Kestrel servers** — requires careful port management and startup sequencing in the test fixture  

### Mitigation Strategies

1. **`data-testid` before E2E** — Add test selectors to Checkout.razor and OrderConfirmation.razor at the start of Cycle 20 (prerequisite for all Playwright assertions)
2. **`WaitForLoadStateAsync(NetworkIdle)`** — Used consistently after every click/navigation to handle Blazor async renders
3. **Trace on failure** — Playwright traces saved as CI artifacts on test failure
4. **Nightly schedule** — E2E tests run nightly on `main` to catch regressions without blocking PR velocity

---

## Alternatives Considered

### bUnit for Checkout.razor

**Verdict:** ❌ Rejected

MudBlazor's `MudStepper` requires extensive JS interop that bUnit's simulated renderer cannot handle reliably. bUnit is correct for isolated, parameter-driven components — not for the checkout wizard. The bugs that matter most (auth redirect, stepper navigation, SignalR delivery) are invisible to bUnit.

**Appropriate use for bUnit:** Pure presentation components like `<OrderSummaryCard>`, `<PriceSummaryRow>` if extracted in a future cycle.

### Selenium

**Verdict:** ❌ Rejected

Selenium requires a standalone WebDriver server. Playwright's auto-wait semantics eliminate 80% of the flakiness that plagues Selenium-based test suites. Playwright's .NET SDK is first-class. No reason to choose Selenium for a greenfield E2E suite in 2026.

### Running Full Docker Compose (`--profile all`)

**Verdict:** ❌ Rejected

Full docker-compose (8+ services) introduces:
- Non-deterministic startup order
- Port conflicts across test runs
- Rebuild time overhead
- No control over stub vs real downstream BC behavior

The in-process Kestrel + TestContainers approach is faster, more reliable, and maintains stub control over Shopping, Orders, Catalog, and Customer Identity BCs.

---

## Implementation Requirements

### Prerequisite: `data-testid` Attributes

Before any Playwright assertion is written, add `data-testid` to MudBlazor components in:
- `Checkout.razor` — stepper steps, address select, shipping radio group, payment token field, action buttons
- `OrderConfirmation.razor` — order ID display, order status chip, real-time update notification area

Without stable selectors, Playwright tests will couple to MudBlazor's internal CSS class structure and break on every MudBlazor patch release.

### New Test Project

```
tests/Customer Experience/Storefront.E2ETests/
├── Storefront.E2ETests.csproj
├── WellKnownTestData.cs
├── E2ETestFixture.cs
├── Hooks/
│   ├── PlaywrightHooks.cs          ← [BeforeScenario] browser & page lifecycle
│   └── DataHooks.cs                ← [BeforeScenario] test data seeding
├── Pages/                          ← Page Object Models (Playwright selectors)
│   ├── LoginPage.cs
│   ├── CartPage.cs
│   ├── CheckoutPage.cs
│   └── OrderConfirmationPage.cs
└── Features/
    ├── checkout-flow.feature       ← Gherkin spec (Phase 1 scenarios)
    └── CheckoutFlowStepDefinitions.cs   ← Reqnroll bindings → Page Objects
```

### New GitHub Actions Workflow

`.github/workflows/e2e.yml` — triggers on `main` push and nightly schedule. NOT triggered on every PR. Uploads Playwright traces as artifacts on failure.

### Package Additions

- `Microsoft.Playwright` — browser automation
- `Microsoft.AspNetCore.Mvc.Testing` — `WebApplicationFactory` for real Kestrel

---

## References

- [Microsoft Playwright .NET Documentation](https://playwright.dev/dotnet/)
- [ADR 0006: Reqnroll for BDD Testing](./0006-reqnroll-bdd-framework.md)
- [ADR 0013: SignalR Migration](./0013-signalr-migration-from-sse.md)
- [docs/features/customer-experience/checkout-flow.feature](../features/customer-experience/checkout-flow.feature)
- [docs/skills/reqnroll-bdd-testing.md](../skills/reqnroll-bdd-testing.md)
- [docs/skills/critterstack-testing-patterns.md](../skills/critterstack-testing-patterns.md)
- [GitHub Issue #58 — Automated browser tests](https://github.com/erikshafer/CritterSupply/issues/58)
- [SignalRNotificationTests.cs — documented E2E gap](../../../tests/Customer%20Experience/Storefront.Api.IntegrationTests/SignalRNotificationTests.cs)

---

**Decision Made By:** Erik Shafer / GitHub Copilot (Senior QA Engineer + Principal Architect consultation)  
**Approved By:** [To be updated after team review]
