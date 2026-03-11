# Cycle 23 Plan: Vendor Portal E2E Testing

**Planned Start:** TBD
**Estimated Duration:** 3–4 days (4 phases with sign-off checkpoints)
**Status:** 📋 PLANNED

---

## Objectives

**Primary Goal:** Add Playwright E2E browser tests for VendorPortal.Web (Blazor WASM), covering
the critical user journeys: JWT login, dashboard with SignalR real-time updates, change request
lifecycle, role-based access, and settings management.

**Why E2E:** Cycle 22 delivered 143 integration tests covering API-level behavior with 100% pass
rate. However, these tests cannot verify:
- Browser-side JWT storage and injection into API calls
- SignalR WebSocket establishment with `?access_token=` query string auth
- MudBlazor component interactions (switches, chips, tables, form validation)
- Cross-origin CORS behavior (WASM on port X calling APIs on different ports)
- Full login → dashboard → submit change request → see result user flow
- Client-side auth guard redirects (`RedirectToLogin` component)

**Success Criteria:**
- [ ] E2E test fixture starts all 3 servers (VendorIdentity.Api, VendorPortal.Api, VendorPortal.Web)
- [ ] Login E2E scenario passes with real JWT auth
- [ ] Dashboard KPI display + SignalR real-time update scenario passes
- [ ] Change request submit flow scenario passes
- [ ] Role-based access (ReadOnly cannot submit) scenario passes
- [ ] Protected route redirect scenario passes
- [ ] All E2E tests pass in CI (GitHub Actions)

---

## Architectural Decisions

### AD1 — Three-Server E2E Fixture (vs. Two-Server Storefront Pattern)

**Context:** The Storefront E2ETests uses a single `E2ETestFixture` starting 2 Kestrel servers
(Storefront.Api + Storefront.Web). The Vendor Portal requires THREE servers because authentication
is a separate bounded context (VendorIdentity.Api) that issues real JWT tokens.

**Decision:** Single `VendorPortalE2ETestFixture` managing all 3 servers + 1 Postgres container.

```
Playwright Browser (Chromium)
       │
       ▼
VendorPortal.Web (WASM static files served by thin Kestrel host, random port)
       │ (cross-origin HTTP + WebSocket)
       ├───────────────────────────────────┐
       ▼                                   ▼
VendorPortal.Api                    VendorIdentity.Api
(real Kestrel, random port)         (real Kestrel, random port)
├── Marten (vendorportal schema)    ├── EF Core (vendor_identity schema)
├── SignalR Hub (/hub/vendor-portal) ├── JWT Token Issuance
├── Wolverine (local only)          ├── Demo Account Seeding
└── JWT Bearer validation           └── Refresh Token Cookies
       │                                   │
       └──────── Shared PostgreSQL ────────┘
                 (TestContainers)
```

**Rationale:**
- **Real JWT flow:** Browser login → VendorIdentity.Api issues real JWT → VendorPortal.Api validates
  real JWT → SignalR authenticates with real token. No stubs for auth.
- **Real SignalR:** WebSocket `?access_token=` auth requires real Kestrel (not `TestServer`).
- **Schema isolation:** Single Postgres container, two schemas (`vendor_identity` for EF Core,
  `vendorportal` for Marten) — matches production topology.
- **External BCs stubbed:** RabbitMQ disabled via `DisableAllExternalWolverineTransports()`.
  Integration events from Catalog/Orders/Inventory BCs injected directly via `IDocumentSession`
  or `IHost.Services` message bus — same pattern as Storefront E2E.

**Alternative rejected:** Running VendorIdentity.Api as a stub. This would undermine the entire
point of E2E testing JWT — we'd be testing fake auth, not real auth.

---

### AD2 — WASM Static File Hosting in Tests

**Context:** VendorPortal.Web is a Blazor WebAssembly app (`Microsoft.NET.Sdk.BlazorWebAssembly`).
In production, Nginx serves the compiled static files. There is no Kestrel runtime, no `Program.cs`
executing server-side, and no `WebApplicationFactory<T>` entry point.

The Storefront uses Blazor Server, which CAN be hosted via `WebApplicationFactory<StorefrontWebMarker>`
because it runs a .NET server process. WASM cannot.

**Decision:** Create a thin ASP.NET Core static file host in the E2E test project that serves the
published WASM output with a dynamically-generated `appsettings.json`.

```csharp
/// <summary>
/// Minimal Kestrel server that serves VendorPortal.Web's published WASM static files.
/// Replaces Nginx for E2E test purposes — allows random port binding and dynamic API URL injection.
///
/// Architecture:
/// 1. dotnet publish VendorPortal.Web → /bin/.../publish/wwwroot/
/// 2. This host serves those files via UseStaticFiles()
/// 3. Before startup, writes appsettings.json with test API URLs
/// 4. WASM app fetches appsettings.json from same origin → picks up test URLs
/// </summary>
public sealed class VendorPortalWasmTestHost : IAsyncDisposable
{
    private WebApplication? _app;
    public string BaseUrl { get; private set; }

    public async Task StartAsync(string identityApiUrl, string portalApiUrl)
    {
        var publishDir = FindPublishedWasmDirectory();

        // Write test-specific appsettings.json into the published wwwroot
        var appSettingsPath = Path.Combine(publishDir, "appsettings.json");
        var testConfig = JsonSerializer.Serialize(new
        {
            ApiClients = new
            {
                VendorIdentityApiUrl = identityApiUrl,
                VendorPortalApiUrl = portalApiUrl
            }
        });
        await File.WriteAllTextAsync(appSettingsPath, testConfig);

        // Build minimal static file server
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseKestrel(k => k.ListenAnyIP(0)); // Random port
        _app = builder.Build();
        _app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(publishDir),
            ServeUnknownFileTypes = true // .wasm, .dat, .blat files
        });
        // Blazor WASM SPA fallback — serve index.html for unmatched routes
        _app.MapFallbackToFile("index.html", new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(publishDir)
        });

        await _app.StartAsync();
        BaseUrl = NormalizeAddress(_app.Urls.First());
    }
}
```

**Why this works:**
- VendorPortal.Web's `Program.cs` already reads from `IConfiguration`:
  ```csharp
  var identityApiUrl = builder.Configuration["ApiClients:VendorIdentityApiUrl"]
      ?? "http://localhost:5240";
  ```
- Blazor WASM's `IConfiguration` fetches `appsettings.json` via HTTP from its own origin
- We control the served `appsettings.json` → we control the API URLs the WASM app uses
- Random port binding eliminates port conflicts in CI

**Prerequisite:** The VendorPortal.Web project must be published before tests run. Add a build
target or pre-test script:
```xml
<Target Name="PublishWasmForE2E" BeforeTargets="Build"
        Condition="'$(IsE2ETestProject)' == 'true'">
  <Exec Command="dotnet publish ../VendorPortal.Web -c Release -o $(WasmPublishDir)" />
</Target>
```

**Alternative rejected:** Hosting WASM via `WebApplicationFactory<App>`. Blazor WASM's `App` is a
Razor component, not an ASP.NET Core host. `WebApplicationFactory` expects `IHost`-compatible
entry points.

---

### AD3 — Real Authentication (No Auth Stubs)

**Context:** The Storefront E2E tests stub `ICustomerIdentityClient` with a handler that always
returns Alice's credentials. This was pragmatic for Cycle 20 (single-user scope). The Vendor Portal
has a richer auth model: 3 demo accounts with different roles (Admin, CatalogManager, ReadOnly),
JWT with custom claims, and refresh tokens.

**Decision:** Use REAL VendorIdentity.Api for authentication in E2E tests. No auth stubs.

**Why:**
1. JWT claims (`VendorTenantId`, `VendorUserId`, `VendorTenantStatus`, `Role`) must be real for
   VendorPortal.Api's authorization policies and SignalR hub group membership to work.
2. Role-based E2E scenarios (ReadOnly can't submit change requests) require real role claims.
3. SignalR `?access_token=` authentication validates the real JWT — stubs would bypass this.
4. Token refresh flow (background timer) needs a real refresh endpoint.
5. Cross-origin cookie handling (HttpOnly `vendor_refresh_token`) must work with real CORS config.

**Test data:** VendorIdentity.Api already seeds demo accounts on startup in Development mode:
- `admin@acmepets.test` / `password` — Admin role
- `catalog@acmepets.test` / `password` — CatalogManager role
- `readonly@acmepets.test` / `password` — ReadOnly role

These are deterministic and idempotent — same accounts on every test run.

**Well-known constants:** Define in `WellKnownTestData.cs`:
```csharp
internal static class VendorAccounts
{
    // Acme Pet Supplies tenant
    public static readonly Guid AcmeTenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    public const string TenantName = "Acme Pet Supplies";

    // Admin user
    public static readonly Guid AdminUserId = Guid.Parse("00000000-0000-0000-0000-000000000010");
    public const string AdminEmail = "admin@acmepets.test";
    public const string AdminPassword = "password";

    // Catalog Manager user
    public static readonly Guid CatalogManagerUserId = Guid.Parse("00000000-0000-0000-0000-000000000011");
    public const string CatalogManagerEmail = "catalog@acmepets.test";

    // ReadOnly user
    public static readonly Guid ReadOnlyUserId = Guid.Parse("00000000-0000-0000-0000-000000000012");
    public const string ReadOnlyEmail = "readonly@acmepets.test";

    public const string SharedPassword = "password";
}
```

---

### AD4 — Test Data Seeding Strategy

**Context:** Two databases need seeding:
1. **VendorIdentity** (EF Core) — tenant + user accounts → seeded by `VendorIdentitySeedData`
   on startup (already exists, runs automatically in Development environment).
2. **VendorPortal** (Marten) — change requests, analytics data, alerts → must be seeded per-scenario.

**Decision:** Layered seeding approach:

| Layer | What | When | How |
|-------|------|------|-----|
| **Infrastructure** | Demo accounts (3 users, 1 tenant) | `BeforeTestRun` | `VendorIdentitySeedData.SeedAsync()` runs automatically on VendorIdentity.Api startup |
| **VendorAccount** | Notification preferences, saved views | `BeforeTestRun` | Inject `VendorTenantCreated` message into VendorPortal.Api's Wolverine bus (triggers `VendorTenantCreatedHandler`) |
| **Scenario-specific** | Change requests, SKU catalog, alerts | `BeforeScenario` (tagged) | Direct Marten `IDocumentSession` writes for read models; Wolverine message execution for write models |

**Direct Marten seeding example** (for change requests list scenario):
```csharp
[BeforeScenario("change-requests", Order = 3)]
public async Task SeedChangeRequestData()
{
    using var session = _fixture.GetMartenSession();

    session.Store(new ChangeRequest
    {
        Id = WellKnownTestData.ChangeRequests.DraftRequestId,
        VendorTenantId = WellKnownTestData.VendorAccounts.AcmeTenantId,
        Sku = "DOG-FOOD-001",
        Type = ChangeRequestType.Description,
        Title = "Update description for premium dog food",
        Status = ChangeRequestStatus.Draft,
        CreatedAt = DateTimeOffset.UtcNow.AddHours(-2)
    });

    session.Store(new ChangeRequest
    {
        Id = WellKnownTestData.ChangeRequests.SubmittedRequestId,
        VendorTenantId = WellKnownTestData.VendorAccounts.AcmeTenantId,
        Sku = "CAT-TOY-003",
        Type = ChangeRequestType.Image,
        Title = "New product images for interactive cat toy",
        Status = ChangeRequestStatus.Submitted,
        CreatedAt = DateTimeOffset.UtcNow.AddHours(-1)
    });

    await session.SaveChangesAsync();
}
```

**Why not API calls for seeding:** Seeding via API requires an authenticated HTTP request for each
document. Direct Marten writes are 10x faster and don't couple test setup to HTTP endpoint behavior
(which itself is under test). The Storefront uses stubs because it doesn't own its data — the Vendor
Portal DOES own its data, so direct Marten access is appropriate.

---

### AD5 — SignalR E2E Testing Strategy

**Context:** The Storefront E2E tests proved that SignalR can be tested E2E by:
1. Accessing `IHubContext<StorefrontHub>` from the test fixture's `IHost`
2. Sending messages to named groups
3. Asserting DOM updates in Playwright

The Vendor Portal's SignalR is more complex:
- JWT-authenticated hub (not session-based)
- Dual group membership (`vendor:{tenantId}` + `user:{userId}`)
- Multiple message types (alerts, metrics, decisions, status updates)
- Reconnection behavior (hub-disconnected-banner)

**Decision:** Test SignalR with the same `IHubContext` injection pattern, targeting both group types.

**Critical difference from Storefront:** The WASM app's SignalR connection uses `?access_token=`
query string authentication. This means:
1. The browser must be logged in (real JWT in `VendorAuthState`)
2. `VendorHubService.ConnectAsync()` must have been called (happens on Dashboard load)
3. The hub's `OnConnectedAsync()` must have extracted claims and joined groups
4. Only then can test-injected messages reach the browser

**Test pattern:**
```csharp
[When(@"the system raises a low stock alert for SKU ""(.*)""")]
public async Task WhenLowStockAlertRaised(string sku)
{
    var hubContext = _fixture.VendorPortalApiHost.Services
        .GetRequiredService<IHubContext<VendorPortalHub>>();

    await hubContext.Clients
        .Group($"vendor:{WellKnownTestData.VendorAccounts.AcmeTenantId}")
        .SendAsync("ReceiveMessage", new
        {
            specversion = "1.0",
            type = "VendorPortal.RealTime.LowStockAlertRaised",
            source = "vendor-portal-api",
            id = Guid.NewGuid().ToString(),
            time = DateTimeOffset.UtcNow,
            datacontenttype = "application/json",
            data = new { sku, tenantId = WellKnownTestData.VendorAccounts.AcmeTenantId }
        });
}

[Then(@"the low stock alert count should increase within (.*) seconds")]
public async Task ThenLowStockAlertCountIncrease(int timeoutSeconds)
{
    // Assert via Playwright DOM polling — same pattern as Storefront
    await Page.WaitForFunctionAsync(
        "([sel, min]) => { const el = document.querySelector(sel); " +
        "const n = parseInt(el?.innerText ?? '0', 10); return n >= min; }",
        new object[] { "[aria-label^='Low Stock Alerts']", 1 },
        new PageWaitForFunctionOptions { Timeout = timeoutSeconds * 1_000 });
}
```

**Scenarios to test:**
1. Dashboard receives `LowStockAlertRaised` → KPI count increments
2. Dashboard receives `SalesMetricUpdated` → banner appears with refresh button
3. Change request decision notification (`ChangeRequestDecisionPersonal`) → toast appears
4. Hub disconnection simulation → `hub-disconnected-banner` appears → reconnect button works

**What NOT to test E2E (keep as integration tests):**
- All 7 SignalR message type routing rules (tested in VendorPortal.Api.IntegrationTests)
- Tenant isolation of hub groups (tested in integration tests)
- Hub connection rejection for suspended/terminated tenants (tested in integration tests)

---

## Project Structure

```
tests/Vendor Experience/VendorPortal.E2ETests/
├── VendorPortalE2ETestFixture.cs          # 3-server lifecycle + Postgres container
├── WellKnownTestData.cs                    # Deterministic IDs, emails, passwords
├── ScenarioContextKeys.cs                  # Reqnroll context key constants
├── VendorPortal.E2ETests.csproj            # Dependencies + WASM publish target
├── xunit.runner.json                       # Serial execution (no parallelization)
│
├── Features/                               # Gherkin + step definitions (colocated)
│   ├── vendor-login.feature                # Auth scenarios
│   ├── VendorLoginStepDefinitions.cs
│   ├── vendor-dashboard.feature            # Dashboard + SignalR scenarios
│   ├── VendorDashboardStepDefinitions.cs
│   ├── vendor-change-requests.feature      # CR lifecycle scenarios
│   ├── VendorChangeRequestStepDefinitions.cs
│   ├── vendor-settings.feature             # Settings scenarios
│   ├── VendorSettingsStepDefinitions.cs
│   ├── vendor-protected-routes.feature     # Auth guard scenarios
│   └── VendorProtectedRoutesStepDefinitions.cs
│
├── Hooks/                                  # Reqnroll lifecycle hooks
│   ├── DataHooks.cs                        # Fixture lifecycle, seeding, cleanup
│   └── PlaywrightHooks.cs                  # Browser context, tracing
│
├── Pages/                                  # Page Object Models
│   ├── VendorLoginPage.cs                  # Login form interactions
│   ├── DashboardPage.cs                    # KPI cards, quick actions, hub state
│   ├── ChangeRequestsPage.cs              # List, filters, table actions
│   ├── SubmitChangeRequestPage.cs          # Form fields, draft/submit
│   ├── ChangeRequestDetailPage.cs          # Detail view, state actions
│   └── SettingsPage.cs                     # Preferences, saved views
│
└── Infrastructure/                         # Test server factories
    ├── VendorIdentityApiKestrelFactory.cs  # WAF + Kestrel for VendorIdentity.Api
    ├── VendorPortalApiKestrelFactory.cs    # WAF + Kestrel for VendorPortal.Api
    └── VendorPortalWasmTestHost.cs         # Static file server for WASM
```

---

## Phase Plan

### Phase 1 — Test Infrastructure & Login Scenarios

**Deliverables:**
- [ ] `VendorPortal.E2ETests.csproj` with all dependencies
- [ ] `VendorPortalE2ETestFixture` — 3 servers + Postgres container lifecycle
- [ ] `VendorIdentityApiKestrelFactory` — real Kestrel, EF Core migrations, demo seeding
- [ ] `VendorPortalApiKestrelFactory` — real Kestrel, Marten, SignalR, disabled RabbitMQ
- [ ] `VendorPortalWasmTestHost` — static file server for published WASM
- [ ] `WellKnownTestData.cs` — vendor accounts, tenant ID, demo SKUs
- [ ] `DataHooks.cs` + `PlaywrightHooks.cs` — lifecycle management
- [ ] `VendorLoginPage.cs` — Page Object Model
- [ ] `vendor-login.feature` + step definitions

**Feature: Vendor Login (E2E)**

```gherkin
Feature: Vendor Login
  As a vendor user
  I want to log in with my email and password
  So that I can access the Vendor Portal dashboard

  Scenario: Successful login as Admin redirects to dashboard
    When I navigate to the login page
    And I enter "admin@acmepets.test" as the email
    And I enter "password" as the password
    And I click "Sign In"
    Then I should be redirected to the dashboard
    And I should see the tenant name "Acme Pet Supplies"

  Scenario: Successful login as ReadOnly user
    When I navigate to the login page
    And I log in as "readonly@acmepets.test"
    Then I should be redirected to the dashboard

  Scenario: Failed login shows error message
    When I navigate to the login page
    And I enter "wrong@example.com" as the email
    And I enter "wrongpassword" as the password
    And I click "Sign In"
    Then I should see a login error message
    And I should remain on the login page

  Scenario: Demo account information is displayed
    When I navigate to the login page
    Then I should see the demo account credentials
```

**CORS Configuration (critical for WASM):**

Both VendorIdentity.Api and VendorPortal.Api have CORS configured for `http://localhost:5241`.
In tests, VendorPortal.Web runs on a random port. The Kestrel factories must override CORS to
allow the test WASM origin:

```csharp
// In VendorPortalApiKestrelFactory.ConfigureWebHost:
services.AddCors(opts => opts.AddDefaultPolicy(policy =>
    policy.WithOrigins(wasmTestHostUrl)
          .AllowAnyHeader()
          .AllowAnyMethod()
          .AllowCredentials()));   // Required for refresh token cookie
```

**Sign-off criteria:** Login scenarios pass; all 3 servers start and stop cleanly.

---

### Phase 2 — Dashboard & SignalR Scenarios

**Deliverables:**
- [ ] `DashboardPage.cs` — Page Object Model (KPI cards, quick actions, hub state)
- [ ] `vendor-dashboard.feature` + step definitions
- [ ] SignalR message injection helpers

**Feature: Vendor Dashboard (E2E)**

```gherkin
Feature: Vendor Dashboard
  As a logged-in vendor user
  I want to see my dashboard with KPI cards and receive real-time updates
  So that I can monitor my business at a glance

  Background:
    Given I am logged in as "admin@acmepets.test"

  Scenario: Dashboard displays KPI cards after login
    Then I should see the dashboard
    And I should see the "Low Stock Alerts" KPI card
    And I should see the "Pending Change Requests" KPI card
    And I should see the "Total SKUs" KPI card

  Scenario: Dashboard quick actions navigate to correct pages
    When I click the "Submit Change Request" quick action
    Then I should be on the submit change request page
    When I navigate back to the dashboard
    And I click the "View Change Requests" quick action
    Then I should be on the change requests list page
    When I navigate back to the dashboard
    And I click the "Settings" quick action
    Then I should be on the settings page

  @signalr
  Scenario: Dashboard receives real-time low stock alert
    And the SignalR connection is established
    When the system raises a low stock alert for SKU "DOG-FOOD-001"
    Then the low stock alert count should increase within 5 seconds

  @signalr
  Scenario: Dashboard shows sales metric update banner
    And the SignalR connection is established
    When the system publishes a sales metric update
    Then I should see the sales metric updated banner within 5 seconds

  @signalr
  Scenario: Hub disconnection shows reconnect banner
    And the SignalR connection is established
    When the SignalR hub connection is interrupted
    Then I should see the hub disconnected banner
```

**SignalR timing considerations (from Storefront E2E lessons):**
- WebSocket establishment: 15s timeout (JWT validation + group join round-trip)
- DOM assertion after message: 5s timeout (WASM rendering is slightly slower than Server)
- Hub disconnection detection: depends on `VendorHubService` reconnect timer

**Sign-off criteria:** KPI cards render; at least one SignalR scenario passes end-to-end.

---

### Phase 3 — Change Request Lifecycle & Role-Based Access

**Deliverables:**
- [ ] `ChangeRequestsPage.cs` — list page POM
- [ ] `SubmitChangeRequestPage.cs` — form page POM
- [ ] `ChangeRequestDetailPage.cs` — detail page POM
- [ ] `vendor-change-requests.feature` + step definitions
- [ ] Role-based access scenarios

**Feature: Change Request Lifecycle (E2E)**

```gherkin
Feature: Change Request Lifecycle
  As a vendor catalog manager
  I want to submit product change requests through the portal
  So that the CritterSupply catalog team can review my updates

  Background:
    Given I am logged in as "admin@acmepets.test"

  @change-requests
  Scenario: Submit a new change request (draft then submit)
    When I navigate to the submit change request page
    And I fill in the change request form:
      | Field   | Value                                  |
      | SKU     | DOG-FOOD-001                           |
      | Type    | Description                            |
      | Title   | Update premium dog food description    |
      | Details | New nutritional information to include. |
    And I click "Save as Draft"
    Then I should see a success message
    When I navigate to the change requests list
    Then I should see a change request with title "Update premium dog food description" in "Draft" status
    When I click view on that change request
    And I click "Submit" on the detail page
    Then the change request status should be "Submitted"

  @change-requests
  Scenario: View change requests with status filter
    Given the following change requests exist:
      | SKU         | Title                | Status    |
      | DOG-FOOD-01 | Update description   | Draft     |
      | CAT-TOY-03  | New product images   | Submitted |
      | BIRD-SEED-7 | Fix weight data      | Approved  |
    When I navigate to the change requests list
    Then I should see 3 change requests
    When I click the "Submitted" filter chip
    Then I should see 1 change request
    And the change request should have SKU "CAT-TOY-03"

  @change-requests
  Scenario: ReadOnly user cannot submit change requests
    Given I am logged in as "readonly@acmepets.test"
    When I navigate to the change requests list
    Then the "Submit Change Request" button should not be visible

  @change-requests
  Scenario: Form validation prevents incomplete submission
    When I navigate to the submit change request page
    And I click "Submit Request" without filling required fields
    Then I should see validation errors for SKU, Type, Title, and Details
```

**MudBlazor interaction caution (from Storefront Cycle 20 lesson):**
The Storefront E2E tests discovered that MudSelect dropdowns do not reliably open in Playwright.
The `type-select` field on SubmitChangeRequest.razor uses `MudSelect`. Mitigation strategies:
1. **Try ARIA-based interaction first:** `page.GetByRole(AriaRole.Combobox)` + keyboard navigation
2. **If MudSelect fails:** Use `data-testid` click + wait for listbox popover with generous timeouts
3. **Last resort:** Tag flaky MudSelect scenarios with `@ignore` and document the limitation (same
   as Storefront's checkout flow), relying on integration tests for the submit endpoint behavior

**Sign-off criteria:** Draft→Submit flow passes; filter chip interaction works; role-based access
verified.

---

### Phase 4 — Settings, Protected Routes & CI Integration

**Deliverables:**
- [ ] `SettingsPage.cs` — Page Object Model
- [ ] `vendor-settings.feature` + step definitions
- [ ] `vendor-protected-routes.feature` + step definitions
- [ ] GitHub Actions workflow step for E2E tests
- [ ] Playwright trace upload for failed scenarios (CI artifact)
- [ ] Documentation updates: CONTEXTS.md, README, ADR

**Feature: Settings (E2E)**

```gherkin
Feature: Vendor Settings
  As a vendor user
  I want to manage my notification preferences and saved dashboard views
  So that I can customize my portal experience

  Background:
    Given I am logged in as "admin@acmepets.test"
    And I navigate to the settings page

  @settings
  Scenario: Toggle notification preferences
    When I toggle "Low Stock Alerts" off
    And I click "Save Preferences"
    Then I should see a success message
    When I reload the settings page
    Then "Low Stock Alerts" should be toggled off

  @settings
  Scenario: Empty saved views shows helpful message
    Then I should see the "No saved views yet" message
```

**Feature: Protected Routes (E2E)**

```gherkin
Feature: Vendor Protected Routes
  As an unauthenticated visitor
  I want to be redirected to login when accessing protected pages
  So that the portal remains secure

  @auth
  Scenario: Unauthenticated user is redirected from dashboard
    Given I am not logged in
    When I navigate directly to "/dashboard"
    Then I should be redirected to "/login"

  @auth
  Scenario: Unauthenticated user is redirected from change requests
    Given I am not logged in
    When I navigate directly to "/change-requests"
    Then I should be redirected to "/login"

  @auth
  Scenario: Unauthenticated user is redirected from settings
    Given I am not logged in
    When I navigate directly to "/settings"
    Then I should be redirected to "/login"
```

**CI Configuration:**
```yaml
# In existing GitHub Actions workflow (or new E2E-specific workflow)
- name: Run Vendor Portal E2E Tests
  run: |
    dotnet publish src/Vendor\ Portal/VendorPortal.Web -c Release
    dotnet test tests/Vendor\ Experience/VendorPortal.E2ETests \
      --configuration Release \
      --logger "trx;LogFileName=vendorportal-e2e.trx"
  env:
    PLAYWRIGHT_HEADLESS: true

- name: Upload Playwright Traces (on failure)
  if: failure()
  uses: actions/upload-artifact@v4
  with:
    name: playwright-traces-vendor-portal
    path: tests/Vendor Experience/VendorPortal.E2ETests/playwright-traces/
```

**Sign-off criteria:** All scenarios pass; CI pipeline runs E2E tests successfully; documentation
updated.

---

## What Stays as Integration Tests (NOT E2E)

These are already covered by the 143 integration tests from Cycle 22 and should NOT be duplicated
in E2E:

| Area | Why Integration Tests Are Sufficient |
|------|--------------------------------------|
| Change request 7-state machine | All state transitions tested via `ExecuteMessageAsync` — no browser needed |
| Auto-supersede (one active per SKU+Type) | Invariant logic is in the handler, not the UI |
| Tenant isolation | Cross-tenant access denial tested via JWT claim manipulation |
| Analytics projection accuracy | `OrderPlaced` → `ProductPerformanceSummary` fan-out is API-level |
| Inventory snapshot updates | Message handler logic, no UI rendering involved |
| Token refresh mechanics | Refresh endpoint tested in VendorIdentity.Api.IntegrationTests |
| Last-admin protection | Role change/deactivation guards are API-level invariants |
| Catalog BC response handling | 7 message types × approve/reject — handler tests with `TrackMessageAsync` |
| Notification preference CRUD | GET/PUT round-trip tested; UI toggle is thin wrapper |
| Saved dashboard view duplicate guard | 409 Conflict tested in integration tests |

**Principle:** E2E tests verify that the **browser correctly orchestrates** the API interactions.
Integration tests verify that the **API correctly implements** the business logic. Don't test
the same invariant twice at different levels.

---

## Risk Register

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| MudSelect dropdown doesn't work in Playwright (same as Storefront) | High | Medium | ARIA-based interaction first; `@ignore` + integration test fallback |
| WASM publish adds 30-60s to test startup | Medium | Low | Cache published output; only rebuild on source change |
| Cross-origin cookie handling breaks in test CORS config | Medium | High | Test refresh flow explicitly; match production CORS as closely as possible |
| JWT token expiry during long test runs (15 min access token) | Low | Medium | E2E test suite should complete in <5 minutes; no refresh needed |
| Blazor WASM cold start in Playwright (download .wasm files) | Medium | Medium | First navigation timeout set to 30s; pre-warm with a dummy page load |
| SignalR WebSocket race condition (message before hub connected) | Medium | High | Explicit `WaitForSignalRConnectionAsync()` before message injection |

---

## Dependencies & Prerequisites

1. **VendorPortal.Web must be publishable:** `dotnet publish` must produce a complete `wwwroot/`
   with all WASM files. Verify this works before starting Phase 1.

2. **`appsettings.json` in `wwwroot/`:** Already exists with `ApiClients` section ✅

3. **`data-testid` coverage:** 30 attributes across 5 pages ✅ (inventoried in AD2)

4. **Solution file update:** Add `VendorPortal.E2ETests` to `CritterSupply.slnx` under
   `/Vendor Portal/` folder.

5. **Port allocation:** No new ports needed — all test servers use random port binding (port 0).

---

## Estimated Test Count

> **Updated after detailed QA scenario planning.** See
> [`docs/cycle-23/VENDOR-PORTAL-E2E-TEST-PLAN.md`](../cycle-23/VENDOR-PORTAL-E2E-TEST-PLAN.md)
> for full feature files and prioritized implementation waves.

| Feature File | Scenarios | Priority | Complexity |
|-------------|:---------:|:--------:|------------|
| `vendor-portal-e2e-auth.feature` | 8 | P0–P2 | Low — form fill + redirect |
| `vendor-portal-e2e-dashboard.feature` | 5 | P0–P2 | Medium — KPI + role visibility |
| `vendor-portal-e2e-signalr.feature` | 7 | P0–P2 | High — real-time WebSocket |
| `vendor-portal-e2e-change-request-lifecycle.feature` | 9 | P0–P2 | High — form + detail + actions |
| `vendor-portal-e2e-change-request-list.feature` | 8 | P1–P2 | Medium — table + filter chips |
| `vendor-portal-e2e-rbac.feature` | 7 | P1–P2 | Medium — role-based UI |
| `vendor-portal-e2e-settings.feature` | 5 | P2 | Low — toggle + save |
| **Total** | **49** | | |

**Implementation waves:** P0 (9 scenarios, ~3 days) → P1 (23 scenarios, ~5 days) → P2 (17 scenarios, ~4 days).
Minimum viable suite = P0 wave only. 1 scenario tagged `@mudselect-risk` for known MudSelect issue.

**Comparison:** Storefront E2ETests has 10 scenarios (4 active, 6 `@ignore`/`@wip`).

---

## Documentation Updates Required

- [ ] **CONTEXTS.md:** Add "E2E Testing" section to Vendor Portal BC description
- [ ] **README.md:** Update test count table; add E2E run instructions
- [ ] **ADR 0026:** "Vendor Portal E2E Testing Architecture" (captures AD1-AD5 from this plan)
- [ ] **Cycle 23 Retrospective:** After completion, document lessons learned
