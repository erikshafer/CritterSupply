# E2E Testing with Playwright

Patterns for writing end-to-end (E2E) browser tests in CritterSupply using Microsoft Playwright, Reqnroll, and real Kestrel servers — without spinning up the full Docker Compose stack.

## When to Use This Skill

Use this skill when:
- Testing a complete user journey through the Blazor UI (login → cart → checkout → confirmation)
- Verifying SignalR real-time updates actually reach the browser (not testable with Alba)
- Exercising MudBlazor component interactions that require a real DOM (dropdowns, steppers, animations)
- Writing living documentation for high-value cross-BC user flows
- Catching regressions that only surface when multiple services integrate at the HTTP layer

**Do not use E2E tests for:**
- Single-endpoint API verification — use Alba integration tests instead
- Business logic validation — use pure function unit tests
- Cross-BC message flows without UI involvement — use integration tests with `InvokeMessageAndWaitAsync`

## Core Principles

1. **Real Kestrel, not TestServer** — Playwright's Chromium browser requires a real TCP port; TestServer (the WebApplicationFactory default) does not bind one
2. **Stub downstream BCs at the client boundary** — Replace `IShoppingClient`, `IOrdersClient`, etc. with in-process stubs to eliminate real network dependencies
3. **Page Object Model (POM) with `data-testid`** — Stable selectors that survive MudBlazor version bumps
4. **Deterministic test data** — No random IDs in E2E tests; use `WellKnownTestData` constants throughout
5. **Infrastructure shared across the run** — Kestrel servers and TestContainers start once per `[BeforeTestRun]`, not per scenario

## Architecture

```
Playwright Browser (Chromium, headless)
     │
     ▼
Storefront.Web (real Kestrel, port=0 / random)
     │  "StorefrontApi" named HttpClient
     ▼
Storefront.Api (real Kestrel, port=0 / random)
     ├── IShoppingClient          → StubShoppingClient
     ├── IOrdersClient            → StubOrdersClient
     ├── ICatalogClient           → StubCatalogClient
     └── ICustomerIdentityClient  → StubCustomerIdentityClient
     │
     ▼
TestContainers PostgreSQL
```

`Storefront.Web` has its `StorefrontApi` HttpClient base address repointed to the test `Storefront.Api` instance. The stubs replace all downstream BC HTTP clients. No real Shopping, Orders, Catalog, or Customer Identity services need to run.

## Project Setup

### NuGet Packages

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" />
  <PackageReference Include="Microsoft.NET.Test.Sdk" />
  <PackageReference Include="Microsoft.Playwright" />
  <PackageReference Include="Reqnroll" />
  <PackageReference Include="Reqnroll.xUnit" />
  <PackageReference Include="Shouldly" />
  <PackageReference Include="Testcontainers.PostgreSql" />
  <PackageReference Include="xunit" />
  <PackageReference Include="xunit.runner.visualstudio" />
</ItemGroup>
```

### Browser Installation

Playwright requires a one-time browser download after build. Add a build target to automate it:

```xml
<Target Name="InstallPlaywrightBrowsers" AfterTargets="Build">
  <Exec
    Command="pwsh bin/$(Configuration)/$(TargetFramework)/playwright.ps1 install chromium"
    Condition="'$(PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD)' != '1'" />
</Target>
```

Set `PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1` if browsers are pre-installed (e.g., cached in CI).

**GitHub Actions browser caching:** Playwright browsers are stored in `~/.cache/ms-playwright` on Linux. Use `actions/cache` with the key `playwright-chromium-${{ hashFiles('**/packages.lock.json') }}` to avoid re-downloading on every run. Alternatively, use the `microsoft/playwright-github-action` action which handles caching automatically.

```yaml
- name: Cache Playwright browsers
  uses: actions/cache@v4
  with:
    path: ~/.cache/ms-playwright
    key: playwright-${{ runner.os }}-${{ hashFiles('**/Storefront.E2ETests.csproj') }}
- name: Install Playwright browsers
  run: PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1 || pwsh ./bin/Debug/net10.0/playwright.ps1 install chromium
```

### Blazor WASM Publish Automation (Critical for WASM E2E Tests)

**Problem:** Blazor WebAssembly (WASM) E2E tests require **publish output** (wwwroot with index.html + _framework directory), not just build output. The test fixture's `FindWasmRoot()` method looks for publish directory first.

**Symptom:** E2E tests fail with 404 errors or app loading timeouts because `index.html` is missing from `bin/.../wwwroot` (only present in `bin/.../publish/wwwroot`).

**Solution:** Add an MSBuild target to the E2E test project that automatically publishes the Blazor WASM project before running tests:

```xml
<!-- Publish Blazor WASM before running E2E tests (creates wwwroot with index.html + _framework).
     This target ensures the E2ETestFixture can locate the complete publish output. -->
<Target Name="PublishBlazorWasmForE2E" BeforeTargets="VSTest">
    <Message Text="Publishing Backoffice.Web for E2E tests..." Importance="high" />
    <MSBuild Projects="../../../src/Backoffice/Backoffice.Web/Backoffice.Web.csproj"
             Targets="Publish"
             Properties="Configuration=$(Configuration);PublishDir=$(MSBuildThisFileDirectory)../../../src/Backoffice/Backoffice.Web/bin/$(Configuration)/net10.0/publish/" />
    <Message Text="✅ Backoffice.Web published successfully" Importance="high" />
</Target>
```

**Why This Works:**
- `BeforeTargets="VSTest"` hooks into the `dotnet test` pipeline, ensuring publish runs before test execution
- `$(Configuration)` passes through Debug/Release configuration from the test run
- `PublishDir` explicitly sets the standard publish output path that `E2ETestFixture.FindWasmRoot()` checks first
- Works identically across Windows, macOS, Linux (standard MSBuild constructs)

**Developer Workflow:**
- **Before:** `dotnet publish src/Backoffice/Backoffice.Web && dotnet test tests/Backoffice/Backoffice.E2ETests` (error-prone, easy to forget)
- **After:** `dotnet test tests/Backoffice/Backoffice.E2ETests` (publish happens automatically)

**References:**
- M32.4 Session 1 Retrospective — Pattern discovery and implementation
- `tests/Backoffice/Backoffice.E2ETests/Backoffice.E2ETests.csproj` — Canonical example

### Project References

The E2E test project references all three Customer Experience projects so it can use their `Program.cs` as the `WebApplicationFactory` entry point:

```xml
<ItemGroup>
  <!-- Domain project: notification handlers, client interfaces -->
  <ProjectReference Include="..\..\..\src\Customer Experience\Storefront\Storefront.csproj" />
  <!-- API project: BFF endpoints, Program.cs, hub -->
  <ProjectReference Include="..\..\..\src\Customer Experience\Storefront.Api\Storefront.Api.csproj" />
  <!-- Web project: Blazor UI, Program.cs -->
  <ProjectReference Include="..\..\..\src\Customer Experience\Storefront.Web\Storefront.Web.csproj" />
</ItemGroup>
```

## Pattern 1: WebApplicationFactory with Real Kestrel

The single most important pattern. `WebApplicationFactory<T>` defaults to `TestServer`, which does not bind to a real TCP port. Playwright's browser cannot connect to it, and SignalR's WebSocket upgrade requires a real HTTP server.

### The Fix: `UseKestrel(0)` Before `CreateDefaultClient()`

```csharp
internal sealed class StorefrontApiKestrelFactory(
    string connectionString,
    StubShoppingClient stubShoppingClient,
    // ... other stubs
) : WebApplicationFactory<Storefront.Api.StorefrontHub>
{
    public string ServerAddress { get; private set; } = string.Empty;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.ConfigureMarten(opts => opts.Connection(connectionString));

            // Replace real HTTP clients with stubs
            services.RemoveAndReplaceClient<IShoppingClient>(stubShoppingClient);
            services.RemoveAndReplaceClient<IOrdersClient>(stubOrdersClient);
            // ...

            services.DisableAllExternalWolverineTransports();
        });
    }

    internal void StartKestrel()
    {
        // CRITICAL ORDER: UseKestrel(0) MUST come before CreateDefaultClient().
        // Port=0 asks the OS to assign a free port — no collision risk in parallel runs.
        UseKestrel(0);
        CreateDefaultClient(); // triggers the Kestrel server to start and bind the port

        // Read the actual bound address after startup
        var serverAddresses = Services
            .GetRequiredService<IServer>()
            .Features
            .Get<IServerAddressesFeature>();

        ServerAddress = serverAddresses?.Addresses.FirstOrDefault() ?? string.Empty;
    }
}
```

**Anti-pattern — swapping the order causes a runtime cast error:**

```csharp
// WRONG: CreateDefaultClient() first starts TestServer; UseKestrel() then fails with:
// InvalidCastException: KestrelServerImpl cannot be cast to TestServer
CreateDefaultClient();
UseKestrel(0); // ← too late
```

### Connecting the Two Servers

`Storefront.Web` needs to know where the test `Storefront.Api` is running. Inject the captured address as in-memory configuration:

```csharp
internal sealed class StorefrontWebKestrelFactory(string storefrontApiBaseUrl)
    : WebApplicationFactory<Storefront.Web.StorefrontWebMarker>
{
    public string ServerAddress { get; private set; } = string.Empty;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ApiClients:StorefrontApiUrl"] = storefrontApiBaseUrl
            });
        });

        builder.ConfigureServices(services =>
        {
            // Repoint the named HttpClient to the test API server
            services.ConfigureHttpClientBaseAddress("StorefrontApi", storefrontApiBaseUrl);

            // Stub the Customer Identity external call made by the Web's /api/auth/login endpoint
            services.Configure<HttpClientFactoryOptions>("CustomerIdentityApi", opts =>
            {
                opts.HttpMessageHandlerBuilderActions.Add(b =>
                    b.PrimaryHandler = new StubCustomerIdentityApiHandler());
            });
        });
    }

    internal void StartKestrel()
    {
        UseKestrel(0);
        CreateDefaultClient();

        var serverAddresses = Services
            .GetRequiredService<IServer>()
            .Features
            .Get<IServerAddressesFeature>();

        ServerAddress = serverAddresses?.Addresses.FirstOrDefault() ?? string.Empty;
    }
}
```

### Startup Sequence in `E2ETestFixture.InitializeAsync()`

```csharp
public async Task InitializeAsync()
{
    // 1. Start database
    await _postgres.StartAsync();
    var connectionString = _postgres.GetConnectionString();

    // 2. Start API (must come first — Web needs API's address)
    _apiFactory = new StorefrontApiKestrelFactory(connectionString, ...stubs);
    _apiFactory.StartKestrel();
    StorefrontApiBaseUrl = _apiFactory.ServerAddress;

    // 3. Start Web, pointing at the test API
    _webFactory = new StorefrontWebKestrelFactory(StorefrontApiBaseUrl);
    _webFactory.StartKestrel();
    StorefrontWebBaseUrl = _webFactory.ServerAddress;
}
```

## Pattern 2: Page Object Model (POM) with `data-testid`

Every Page Object Model encapsulates all selectors and interactions for one page. Step definitions only call POM methods — they never write raw Playwright locators.

### Selector Strategy

| ✅ Do | ❌ Don't |
|---|---|
| `page.Locator("[data-testid='checkout-stepper']")` | `page.Locator(".mud-stepper")` |
| `page.GetByLabel("Email")` | `page.Locator("input[type='email']")` |
| `page.GetByRole(AriaRole.Button, new() { Name = "Sign In" })` | `page.Locator(".mud-button-root")` |

MudBlazor CSS class names change with version bumps. `data-testid` attributes are explicit contracts between UI and tests.

### POM Structure

```csharp
public sealed class CheckoutPage(IPage page)
{
    // Locators — private, named for the UI element they represent
    private ILocator CheckoutStepper => page.Locator("[data-testid='checkout-stepper']");
    private ILocator AddressSelect   => page.Locator("[data-testid='address-select']");
    private ILocator SaveAddressButton => page.Locator("[data-testid='btn-save-address']");

    // Public methods — one per user action or assertion
    public async Task NavigateAsync()
    {
        await page.GotoAsync("/checkout");
        await WaitForCheckoutLoadedAsync();
    }

    public async Task WaitForCheckoutLoadedAsync()
    {
        // Wait for success OR error state — both are valid outcomes at load time
        await page.WaitForSelectorAsync(
            "[data-testid='checkout-stepper'], [data-testid='checkout-error']",
            new PageWaitForSelectorOptions { Timeout = 15_000 });
    }

    public async Task<bool> IsLoadedSuccessfullyAsync() =>
        await CheckoutStepper.IsVisibleAsync();
}
```

### Naming Conventions

| UI Element | Locator Name | Method Name |
|---|---|---|
| Checkout stepper container | `CheckoutStepper` | `WaitForCheckoutLoadedAsync()` |
| Address dropdown | `AddressSelect` | `SelectAddressByNicknameAsync(string)` |
| "Save & Continue" on step 1 | `SaveAddressButton` | `ClickSaveAddressAndContinueAsync()` |
| Order totals | `OrderTotal` | `GetOrderTotalAsync()` |

### Test-ID Naming Conventions (data-testid attributes)

**Established:** M32.1 Sessions 14-15 (Backoffice E2E tests)

Use semantic, kebab-case identifiers that describe **what** the element represents, not **how** it looks:

| Element Type | Pattern | Examples | Anti-Pattern |
|--------------|---------|----------|--------------|
| KPI Cards | `kpi-{metric-name}` | `kpi-total-orders`<br>`kpi-revenue`<br>`kpi-pending-returns` | ❌ `card-1`, `metric-box` |
| KPI Values (nested) | `kpi-value` | Always nested within KPI card element | ❌ `value-123`, `number-display` |
| Navigation Links | `nav-{destination}` | `nav-customer-service`<br>`nav-operations`<br>`nav-analytics` | ❌ `button-cs`, `link-1` |
| Form Inputs | `{form}-{field}` | `login-email`<br>`login-password`<br>`checkout-phone` | ❌ `input1`, `email-box` |
| Form Buttons | `{form}-{action}` | `login-submit`<br>`logout-button`<br>`checkout-continue` | ❌ `btn1`, `submit-btn` |
| Real-time Indicators | `realtime-{state}` | `realtime-connected`<br>`realtime-disconnected` | ❌ `hub-status`, `connection-icon` |
| Error/Success Alerts | `{form}-{type}` | `login-error`<br>`checkout-success`<br>`order-confirmation` | ❌ `error-msg`, `alert-1` |
| Loading Indicators | `{form}-loading` | `login-loading`<br>`dashboard-loading`<br>`cart-loading` | ❌ `spinner`, `loader` |

**Rationale:**
- **Semantic names** survive UI refactoring (reordering, restyling, component library changes)
- **Kebab-case** aligns with HTML attribute conventions and is URL-safe
- **Hierarchical structure** (parent-child) makes nested locators robust (e.g., `KpiCard.Locator("[data-testid='kpi-value']")`)
- **Stable identifiers** don't break when CSS classes, colors, fonts, or ordering changes

**Anti-Patterns to Avoid:**
- ❌ Generic names: `button1`, `div-content`, `label-text`
- ❌ Component-specific names: `mud-button`, `mud-card` (these are CSS classes, not test-ids)
- ❌ Presentational names: `red-banner`, `large-font` (describe semantics, not appearance)
- ❌ Index-based names: `kpi-1`, `alert-2` (fragile, breaks on reordering)
- ❌ Mixed conventions: `loginEmail` (camelCase), `Login_Submit` (snake_case with capitals)

**When to Use Nested test-ids:**

Use parent/child test-id hierarchies when an element appears multiple times but each instance has unique meaning:

```razor
<!-- Good: Hierarchical test-ids -->
<MudPaper data-testid="kpi-total-orders">
    <MudText Typo="Typo.h6">Total Orders</MudText>
    <MudText Typo="Typo.h4" data-testid="kpi-value">@_activeOrders</MudText>
</MudPaper>

<MudPaper data-testid="kpi-revenue">
    <MudText Typo="Typo.h6">Revenue</MudText>
    <MudText Typo="Typo.h4" data-testid="kpi-value">@_todaysRevenue.ToString("C0")</MudText>
</MudPaper>
```

```csharp
// Page Object Model usage
private ILocator TotalOrdersCard => _page.GetByTestId("kpi-total-orders");
private ILocator RevenueCard => _page.GetByTestId("kpi-revenue");

public async Task<string?> GetTotalOrdersValueAsync()
{
    // Scoped locator: finds kpi-value within TotalOrdersCard only
    var valueLocator = TotalOrdersCard.Locator("[data-testid='kpi-value']");
    return await valueLocator.TextContentAsync();
}
```

**Reference:** See `tests/Backoffice/Backoffice.E2ETests/Pages/DashboardPage.cs` for complete implementation.

## Pattern 3: MudBlazor `MudSelect` Interaction

MudBlazor's `MudSelect` renders a layered HTML structure. Getting the click right in headless Chromium requires `Force = true` on the inner `.mud-select-input` element. Two timeout constants model the two distinct wait phases.

**Why `Force = true` is required:**  
MudBlazor 9.x renders a transparent `.mud-input-mask` div _above_ `.mud-select-input` in z-order. This mask intercepts real pointer events so MudBlazor's JavaScript can manage dropdown state. Playwright's actionability check calls `elementFromPoint()` at the element's centre — the mask is returned, not `.mud-select-input` — so Playwright waits 30 s for `.mud-select-input` to be "hittable" (it never will be). `Force = true` skips the hit-test entirely and dispatches a synthetic `MouseEvent` directly to `.mud-select-input`, where Blazor's delegated `@onclick` handler is registered. The event bubbles normally and MudBlazor opens the dropdown.

**Why not click the outer wrapper directly?**  
`AddressSelect.ClickAsync()` fires a centre-click on the outer `[data-testid='address-select']` layout container. Under headless Chromium the bounding-box centre frequently lands on the rendered `<label>` text or surrounding padding — MudBlazor receives no "open" event there, so the listbox portal never renders.

Options render inside a **portal popover** at `document.body` level (outside the select container) and only appear in the DOM after the dropdown opens. The `[role='listbox']` wait synchronises the option click with popover render.

### Correct Pattern — Force-click Inner Trigger + Explicit Listbox Wait + data-testid Option

```csharp
// Two-phase timeout: popover appearance (slow) vs option click (fast once popover is open).
private const int MudSelectListboxTimeoutMs = 15_000; // popover open + animation + CI headroom
private const int MudSelectOptionTimeoutMs  = 10_000; // option already in DOM when listbox visible

public async Task SelectAddressByNicknameAsync(string nickname)
{
    // Waits for async data load: the wrapper is only rendered when SavedAddresses.Any() is true.
    await AddressSelect.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

    // Force = true bypasses Playwright's hit-test (blocked by MudBlazor's transparent mask).
    // Scoped through AddressSelect to guard against multiple MudSelects on the page.
    // NOTE: .mud-select-input is an internal MudBlazor class (verified: MudBlazor 9.1.0).
    //       Confirm it still exists before upgrading the MudBlazor package version.
    await AddressSelect.Locator(".mud-select-input").ClickAsync(new LocatorClickOptions { Force = true });

    // Wait for the MudBlazor listbox portal (rendered at document.body, not inside AddressSelect).
    await page.WaitForSelectorAsync("[role='listbox']",
        new PageWaitForSelectorOptions { Timeout = MudSelectListboxTimeoutMs });

    // Target by data-testid — stable across text formatting changes.
    // data-testid="address-option-{nickname.ToLowerInvariant()}" forwarded via MudBlazor UserAttributes.
    var optionLocator = page.Locator($"[data-testid='address-option-{nickname.ToLowerInvariant()}']");
    await optionLocator.ClickAsync(new LocatorClickOptions { Timeout = MudSelectOptionTimeoutMs });
}
```

### Anti-Patterns

```csharp
// ❌ FRAGILE — outer wrapper centre click lands on <label>/padding in headless Chromium;
//              no "open" event reaches MudBlazor; [role='listbox'] never appears.
await AddressSelect.ClickAsync();

// ❌ FRAGILE — Playwright hit-test returns the .mud-input-mask overlay; waits 30s for
//              .mud-select-input to be "hittable" — it never is because the mask is structural.
await AddressSelect.Locator(".mud-select-input").ClickAsync();

// ❌ FRAGILE — text-based: fails when MudBlazor renders full address in the option label.
await page.Locator($"[role='option']:has-text('{nickname}')").ClickAsync();
```

### Why Scope `.mud-select-input` Through `AddressSelect`?

A bare `page.Locator(".mud-select-input")` matches the **first** `.mud-select-input` on the page. Always scope through the known parent locator:

```csharp
await AddressSelect.Locator(".mud-select-input").ClickAsync(new LocatorClickOptions { Force = true });  // ✅ scoped
await page.Locator(".mud-select-input").ClickAsync(new LocatorClickOptions { Force = true });           // ❌ unscoped — fragile
```

### Anti-Pattern: `GetByRole` with Exact Name Match

```csharp
// WRONG — GetByRole requires an EXACT accessible name match.
// The option text is "Home - 123 Main St, Seattle, WA 98101", not "Home".
// This causes a TimeoutException every time.
await page.GetByRole(AriaRole.Option, new() { Name = "Home" }).ClickAsync();
```

## Pattern 4: Login Flow Timing

Blazor's `NavigationManager.NavigateTo(url, forceLoad: true)` performs a full-page reload after the login fetch completes. `WaitForLoadStateAsync(NetworkIdle)` can return between the fetch completing and the hard reload starting — causing the page URL to still read `/login` when checked.

```csharp
public async Task LoginAsync(string email, string password)
{
    await EmailInput.FillAsync(email);
    await PasswordInput.FillAsync(password);
    await LoginButton.ClickAsync();

    // Wait for navigation AWAY from /login to be fully committed first.
    // Using WaitForLoadStateAsync(NetworkIdle) alone races against the forceLoad reload.
    try
    {
        await page.WaitForURLAsync(
            url => !url.Contains("/login"),
            new() { Timeout = 10_000 });

        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }
    catch (TimeoutException)
    {
        // Login may have failed — page stayed at /login.
        // Let IsLoggedInAsync() surface the assertion failure with a clear message.
    }
}
```

## Pattern 5: Stub Coordination for Deterministic IDs

Stubs must produce consistent IDs across the checkout flow. When the browser POSTs to initiate checkout, `StubShoppingClient.InitiateCheckoutAsync()` must return the exact same ID that `StubOrdersClient` has pre-indexed.

```csharp
// In E2ETestFixture.SeedStandardCheckoutScenarioAsync():

// 1. Create cart in Shopping stub
var cartId = await StubShoppingClient.InitializeCartAsync(WellKnownTestData.Customers.Alice);
await StubShoppingClient.AddItemAsync(cartId, "DOG-BOWL-01", quantity: 2, unitPrice: 19.99m);

// 2. Pre-register the deterministic checkout ID for this cart
StubShoppingClient.SetCheckoutId(cartId, WellKnownTestData.Checkouts.AliceCheckoutId);

// 3. Pre-seed Orders stub with the same ID so GetCheckoutAsync() finds it
StubOrdersClient.AddCheckout(
    WellKnownTestData.Checkouts.AliceCheckoutId,
    WellKnownTestData.Customers.Alice,
    new CheckoutItemDto("DOG-BOWL-01", 2, 19.99m));

// 4. Expose cartId so step definitions can inject it into browser localStorage
SeededCartId = cartId;
```

**Inside `StubShoppingClient.InitiateCheckoutAsync()`:**

```csharp
public Task<Guid> InitiateCheckoutAsync(Guid cartId, CancellationToken ct = default)
{
    if (!_carts.TryGetValue(cartId, out var cart)) /* throw */;
    if (!cart.Items.Any()) /* throw */;

    // Return the pre-registered deterministic ID if one was set; otherwise random.
    var checkoutId = _checkoutIds.TryGetValue(cartId, out var id)
        ? id
        : Guid.CreateVersion7();

    return Task.FromResult(checkoutId);
}
```

## Pattern 6: LocalStorage Injection

After login, the browser session needs the `cartId` that was seeded in the fixture. Inject it via `page.EvaluateAsync`:

```csharp
[Given(@"I am logged in as ""(.*)""")]
public async Task GivenIAmLoggedInAs(string email)
{
    await LoginPage.NavigateAsync();
    await LoginPage.LoginAsync(email, WellKnownTestData.Customers.AlicePassword);
    (await LoginPage.IsLoggedInAsync()).ShouldBeTrue();

    // Inject seeded cartId into browser localStorage so Cart.razor can read it
    if (_fixture.SeededCartId.HasValue)
    {
        await Page.EvaluateAsync(
            "cartId => localStorage.setItem('cartId', cartId)",
            _fixture.SeededCartId.Value.ToString());
    }
}
```

## Pattern 7: Wolverine Message Injection for SignalR Tests

Integration tests with `IAlbaHost` use `TrackActivity()`. E2E tests that need to inject Wolverine messages into the running `IHost` use `InvokeMessageAndWaitAsync()` directly on `StorefrontApiHost`:

```csharp
[When(@"the Payments BC publishes a payment authorized event for my order")]
public async Task WhenPaymentsBCPublishesPaymentAuthorized()
{
    var orderId = Guid.Parse(_scenarioContext.Get<string>(ScenarioContextKeys.OrderId));

    // Inject directly into the live IHost — bypasses RabbitMQ, exercises the full handler path
    await _fixture.StorefrontApiHost.InvokeMessageAndWaitAsync(
        new OrderStatusChanged(
            orderId,
            WellKnownTestData.Customers.Alice,
            "PaymentAuthorized",
            DateTimeOffset.UtcNow));
}
```

**Expose `IHost` from the fixture:**

```csharp
public IHost StorefrontApiHost { get; private set; } = null!;

public async Task InitializeAsync()
{
    _apiFactory.StartKestrel();
    StorefrontApiHost = _apiFactory.Services.GetRequiredService<IHost>();
    // ...
}
```

## Pattern 8: SignalR Antiforgery

ASP.NET Core 10+ enables antiforgery validation on all mutation endpoints by default. WebSocket connections are not CSRF-vulnerable (browsers enforce same-origin on WS upgrades), but the default breaks SignalR hub negotiation. Disable it explicitly on the hub mapping:

```csharp
// Storefront.Api/Program.cs
app.MapHub<StorefrontHub>("/hub/storefront")
    .DisableAntiforgery(); // Required: ASP.NET Core 10+ default breaks WebSocket negotiation
```

## Pattern 9: Setup-via-Stub for Multi-Concern Scenarios

When a Gherkin step is **setup** rather than the thing being tested, bypass browser UI and seed data directly into stubs. Navigating through UI for setup purposes couples unrelated concerns and creates fragile dependencies on components that are already covered elsewhere.

**The Principle:**
> "The browser only touches what the test is testing. Everything else (login, cart population, address seeding) is done via API or stub — never via browser UI navigation."
>
> *Exception: login must use the real Blazor auth flow to set the session cookie correctly.*

### Bad: Setup via browser checkout for a SignalR test

```csharp
// WRONG — runs the full 4-step checkout wizard just to get an orderId.
// If MudBlazor's address dropdown has a timing issue (unrelated to SignalR),
// this SignalR test fails, masking what's actually being tested.
[Given(@"I have successfully placed an order")]
public async Task GivenIHaveSuccessfullyPlacedAnOrder()
{
    await CartPage.NavigateAsync();
    await CartPage.ClickProceedToCheckoutAsync();
    await CheckoutPage.SelectAddressByNicknameAsync("Home");
    await CheckoutPage.ClickSaveAddressAndContinueAsync();
    // ... 4 more steps ...
    await CheckoutPage.ClickPlaceOrderAsync();
    // Now finally on the confirmation page — but any of those steps could fail!
}
```

### Good: Setup via stub + direct navigation

```csharp
// CORRECT — seed the order in the fixture, navigate directly.
// The SignalR test focuses on what it's testing: hub delivery to the browser.
// The full checkout UI flow is already covered by Scenario 1 (happy path).
[Given(@"I have successfully placed an order")]
public async Task GivenIHaveSuccessfullyPlacedAnOrder()
{
    // Order is pre-seeded in SeedStandardCheckoutScenarioAsync via StubOrdersClient
    var orderId = WellKnownTestData.Orders.AliceOrderId;
    await Page.GotoAsync($"/order-confirmation/{orderId}");
    await OrderConfirmationPage.WaitForLoadAsync();
    _scenarioContext.Set(orderId.ToString(), ScenarioContextKeys.OrderId);
}
```

**In the fixture (`SeedStandardCheckoutScenarioAsync`):**

```csharp
// Seed a pre-placed order for SignalR scenarios
var total = (WellKnownTestData.Products.CeramicDogBowlPrice * 2)
          + WellKnownTestData.Products.InteractiveCatLaserPrice
          + WellKnownTestData.Shipping.StandardCost;

StubOrdersClient.AddOrder(new OrderDto(
    WellKnownTestData.Orders.AliceOrderId,
    WellKnownTestData.Customers.Alice,
    "Placed",
    DateTimeOffset.UtcNow,
    total));
```

**Key benefits:**
- SignalR test is isolated from checkout UI timing issues
- Faster execution (no 4-step browser checkout per SignalR test)
- Clearer test intent: *"Given an order exists, when a payment event arrives, the page updates"*

---

## Pattern 10: Blazor WASM Timeout Tuning (M32.1 Sessions 15-16)

Blazor WebAssembly (WASM) has fundamentally different cold-start timing characteristics than Blazor Server:

| Blazor Server | Blazor WASM |
|---------------|-------------|
| ✅ SSR: HTML rendered server-side, instant display | ❌ CSR: Browser downloads .NET runtime + assemblies (5-30s) |
| ✅ SignalR connection ready immediately | ❌ SignalR depends on WASM hydration + JWT auth (1-3s delay) |
| ✅ Navigation instant (server-rendered) | ❌ Navigation requires full client-side routing stack |
| ✅ Minimal bundle size (KB) | ❌ Large bundle size (MB) — especially on first load |

**Challenge:** Default 30s Playwright timeouts mask real timing issues. If a test passes with 30s but fails in production with a 10s user patience threshold, the test provides false confidence.

**Solution:** Use a **tiered timeout strategy** that models real-world user behavior and separates infrastructure delays from application responsiveness.

### Tiered Timeout Strategy

| Operation Type | Timeout | Rationale | Example |
|----------------|---------|-----------|---------|
| **Initial page load** | 15s | WASM bundle download + runtime boot + MudBlazor hydration | `NavigateAsync()` in LoginPage.cs |
| **Authenticated navigation** | 15s | JWT auth state propagation + Blazor router + component mount | `LoginAndWaitForDashboardAsync()` |
| **Element visibility** | 10s | Component render + data binding (no network) | `EmailInput.WaitForAsync()` |
| **SignalR connection** | 15s | Depends on JWT auth completion (1-3s) + WebSocket upgrade | `WaitForRealtimeConnectionAsync()` |
| **State checks** | 5s | Immediate DOM queries (no async operations) | `IsRealtimeConnectedAsync()` |
| **KPI updates (real-time)** | 5s | SignalR already connected, just polling DOM for value change | `WaitForKpiUpdateAsync()` |

### LoginPage.cs Example (M32.1 Session 16)

```csharp
public async Task NavigateAsync()
{
    await _page.GotoAsync($"{_baseUrl}/login");

    // Step 1: Wait for network idle (HTTP resources complete)
    await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);

    // Step 2: Wait for MudBlazor initialization (WASM hydration check)
    // MudBlazor renders .mud-dialog-provider only after Blazor runtime is ready
    await _page.WaitForSelectorAsync(".mud-dialog-provider", new() {
        State = WaitForSelectorState.Attached,
        Timeout = 15_000  // ← WASM cold start headroom
    });

    // Step 3: Wait for login form to be interactive (reduced from 30s to 15s)
    await EmailInput.WaitForAsync(new() {
        State = WaitForSelectorState.Visible,
        Timeout = 15_000  // ← Form render after hydration
    });
}

public async Task LoginAndWaitForDashboardAsync(string email, string password)
{
    await EmailInput.FillAsync(email);
    await PasswordInput.FillAsync(password);
    await LoginButton.ClickAsync();

    // Wait for successful navigation to dashboard (increased from 10s to 15s)
    // Auth state propagation takes 1-3s in WASM (in-memory token → AuthenticationStateProvider → Blazor router)
    await _page.WaitForURLAsync(url => url.Contains("/dashboard"), new() { Timeout = 15_000 });

    // Wait for dashboard to be fully loaded (MudBlazor hydration)
    await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
}
```

### DashboardPage.cs Example (M32.1 Session 16)

```csharp
public async Task WaitForRealtimeConnectionAsync()
{
    // Wait for SignalR connection indicator to show "Connected"
    // SignalR connection depends on:
    // 1. JWT auth state fully propagated (1-3s)
    // 2. SignalR client initialization
    // 3. WebSocket upgrade handshake
    // Total: typically 2-5s, but allow 15s for CI headroom
    await RealtimeIndicator.WaitForAsync(new() {
        State = WaitForSelectorState.Visible,
        Timeout = 15_000  // ← Increased from 10s (Session 16)
    });
}

public async Task<bool> IsRealtimeConnectedAsync()
{
    try
    {
        // Fast state check — SignalR connection either exists or doesn't
        await RealtimeIndicator.WaitForAsync(new() {
            State = WaitForSelectorState.Visible,
            Timeout = 2_000  // ← Short timeout for immediate feedback
        });
        return true;
    }
    catch (TimeoutException)
    {
        return false;
    }
}
```

### When to Use Each Timeout

**15s Timeouts (Infrastructure + Hydration):**
- ✅ Initial page navigation to WASM app (`NavigateAsync()`)
- ✅ Navigation after login (auth state propagation)
- ✅ SignalR connection establishment (depends on JWT)
- ✅ MudBlazor component hydration checks (`.mud-dialog-provider`)

**10s Timeouts (Component Rendering):**
- ✅ Element visibility after page load
- ✅ MudSelect dropdown popover appearance
- ✅ Form validation messages

**5s Timeouts (State Checks):**
- ✅ Checking if element is visible (immediate DOM query)
- ✅ Checking connection status (indicator already rendered)
- ✅ KPI value updates via SignalR (connection already active)

**2s Timeouts (Fast Feedback):**
- ✅ Negative assertions (`IsErrorVisibleAsync()` — should fail fast if no error)
- ✅ Polling-based state checks where failure is expected

### MudBlazor Hydration Detection Pattern

WASM apps require explicit hydration checks because `NetworkIdle` only waits for HTTP — not for Blazor runtime initialization:

```csharp
// ❌ WRONG — NetworkIdle alone is insufficient for WASM
await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
await EmailInput.WaitForAsync(new() { Timeout = 30_000 }); // ← Times out in CI

// ✅ CORRECT — Explicit MudBlazor hydration check
await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
await _page.WaitForSelectorAsync(".mud-dialog-provider", new() {
    State = WaitForSelectorState.Attached,
    Timeout = 15_000
}); // ← MudBlazor only renders this after Blazor runtime ready
await EmailInput.WaitForAsync(new() { Timeout = 15_000 });
```

**Why `.mud-dialog-provider`?**
- MudBlazor v9+ renders this element in `MudLayout.razor` during its initialization phase
- It's a stable, non-user-visible element that signals "MudBlazor JS interop is ready"
- Alternative markers: `.mud-popover-provider`, `.mud-theme-provider` (all work)

### Blazor WASM Client-Side Navigation Patterns (M33.0 Phase 7)

Blazor WASM uses client-side routing via Blazor Router, which does **not** trigger browser-level navigation events. This requires different patterns than full-page navigations.

**Key Difference:**
- **Full Page Navigation** (`page.GotoAsync()`) → Entire DOM clears, new HTML loads, `WaitForLoadStateAsync(LoadState.NetworkIdle)` waits for HTTP resources
- **Client-Side Navigation** (clicking `<NavLink>`) → DOM updates via JavaScript, no new HTML loaded, `NetworkIdle` already complete

#### ReturnManagementPage Example (Backoffice.E2ETests)

```csharp
public async Task NavigateFromDashboardAsync()
{
    await ReturnManagementNavLink.ClickAsync();

    // Wait for WASM client-side navigation to complete
    // Blazor WASM routing doesn't trigger full page load events
    await _page.WaitForURLAsync(
        url => url.Contains("/returns"),
        new() { Timeout = 5_000 });  // ← Reduced timeout — no HTTP involved

    await WaitForPageLoadedAsync();
}

public async Task WaitForPageLoadedAsync()
{
    // Wait for MudBlazor framework to hydrate (WASM pattern)
    await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);

    // Wait for MudBlazor provider (CSS framework initialization)
    await _page.WaitForSelectorAsync(".mud-dialog-provider",
        new() { State = WaitForSelectorState.Attached, Timeout = WasmHydrationTimeoutMs });

    // Wait for Return Management page-specific elements
    // Either the status filter (success) OR an error message (failure) should appear
    try
    {
        await _page.WaitForSelectorAsync(
            "[data-testid='status-filter'], [data-testid='authorization-error'], [data-testid='session-expired']",
            new() { Timeout = ApiCallTimeoutMs });
    }
    catch (TimeoutException)
    {
        // Page may have loaded but no returns data yet — this is acceptable
        // Assertions in step definitions will catch actual failures
    }
}
```

**Pattern Breakdown:**

1. **Click NavLink** — Triggers Blazor Router's client-side navigation (no HTTP request)
2. **`WaitForURLAsync(predicate)`** — Polls `page.Url` until the predicate returns true (no `WaitUntil.Commit` needed for WASM)
3. **Reduced Timeout (5s)** — Client-side navigation is fast; no network delay
4. **Post-Navigation Hydration Check** — Still need to wait for MudBlazor + component mount
5. **Multiple Selector Fallback** — Wait for any of: success state, error state, or session expiry (graceful failure handling)

#### Why `WaitForURLAsync` Instead of `WaitForNavigationAsync`

```csharp
// ❌ WRONG — WaitForNavigationAsync expects browser-level navigation (full page reload)
await ReturnManagementNavLink.ClickAsync();
await _page.WaitForNavigationAsync(new() { UrlString = "**/returns" });  // ← Hangs forever

// ✅ CORRECT — WaitForURLAsync polls the URL without expecting navigation events
await ReturnManagementNavLink.ClickAsync();
await _page.WaitForURLAsync(url => url.Contains("/returns"), new() { Timeout = 5_000 });
```

**Why:**
- `WaitForNavigationAsync()` listens for the `Page.FrameNavigated` event (browser signals "I loaded a new document")
- Blazor WASM client-side routing **never fires this event** because the DOM updates in-place via JavaScript
- `WaitForURLAsync(predicate)` actively polls `page.Url` every 100ms, which works for both full-page and client-side navigations

#### Timeout Constants for Backoffice WASM Navigation

```csharp
// Timeout constants for WASM hydration and MudBlazor interactions
private const int WasmHydrationTimeoutMs = 30_000;  // WASM bootstrap + MudBlazor provider
private const int MudSelectListboxTimeoutMs = 15_000;  // MudSelect popover open + animation
private const int ApiCallTimeoutMs = 15_000;  // Network call + response processing
```

**Rationale:**
- **30s for WASM Hydration** — Cold start includes .NET runtime download + Blazor boot + MudBlazor JS interop (5-30s in CI)
- **15s for MudSelect Listbox** — Popover portal renders at `document.body` level; MudBlazor animation + DOM update (1-3s typical, allow CI headroom)
- **15s for API Calls** — Network round-trip + JSON deserialization + component re-render (1-5s typical, allow CI headroom)

#### MudSelect Force-Click Pattern (Transparent Input Mask)

```csharp
public async Task SelectStatusFilterAsync(string status)
{
    await StatusFilter.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = ApiCallTimeoutMs });

    // Force-click inner trigger (MudBlazor pattern from M32 E2E sessions)
    // MudBlazor's transparent .mud-input-mask blocks normal click hit-test
    await StatusFilter.Locator(".mud-select-input")
        .ClickAsync(new LocatorClickOptions { Force = true });

    // Wait for MudBlazor listbox portal to render (at document.body level, not inside StatusFilter)
    await _page.WaitForSelectorAsync("[role='listbox']",
        new() { Timeout = MudSelectListboxTimeoutMs });

    // Click the option by value text
    var optionLocator = _page.Locator($"[role='option']:has-text('{status}')");
    await optionLocator.ClickAsync(new LocatorClickOptions { Timeout = 10_000 });
}
```

**Why Force-Click:**
- MudBlazor v9+ uses a transparent `.mud-input-mask` overlay to handle browser autocomplete prevention
- Playwright's hit-test verification sees the mask as "covering" the input, failing the click
- `Force = true` bypasses hit-test verification and dispatches the click event directly to the target element
- This is **safe** because we've already verified the element is visible and in the viewport

**References:**
- M32.1 Session 5 — Initial MudSelect force-click discovery (Vendor Portal Change Requests filter)
- M32.3 Session 9 — Generalized MudSelect pattern (Customer Search filters)
- M33.0 Session 15 (Phase 7) — Applied to Return Management status filter

### CI-Specific Considerations

**Problem:** Tests pass locally (fast desktop CPU) but timeout in GitHub Actions (shared runners, slower CPUs).

**Solutions:**
1. **Use tiered timeouts** — Don't use 30s for everything; model real operations
2. **Add explicit hydration checks** — Don't assume `NetworkIdle` = "app ready"
3. **Increase only infrastructure timeouts** — 10s → 15s for WASM cold start, not for all operations
4. **Test in headless mode locally** — `PLAYWRIGHT_HEADLESS=true dotnet test` simulates CI environment

### Anti-Patterns

**❌ Uniform 30s Timeouts:**
```csharp
// Hides real timing issues — app might be broken but test passes
await EmailInput.WaitForAsync(new() { Timeout = 30_000 });
await DashboardCard.WaitForAsync(new() { Timeout = 30_000 });
await SignalRIndicator.WaitForAsync(new() { Timeout = 30_000 });
```

**❌ No Hydration Checks:**
```csharp
// Fails in CI because NetworkIdle completes before WASM runtime ready
await _page.GotoAsync("/login");
await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
await EmailInput.WaitForAsync(); // ← Times out
```

**❌ Mixing Initial Load and State Check Timeouts:**
```csharp
// Initial navigation should be 15s, but state check should be 2-5s
await LoginPage.NavigateAsync(); // 15s
await LoginPage.IsErrorVisibleAsync(); // Should be 2s, not 15s
```

### Lessons Learned (M32.1 Sessions 12-16)

1. **Session 12:** E2E tests timed out with 30s defaults — root cause was missing MudBlazor hydration check
2. **Session 13:** Added authorization policies (WASM requires client-side `AddAuthorizationCore()`)
3. **Session 14:** Added `data-testid` attributes (form inputs were missing test-ids)
4. **Session 15:** Fixed JWT role claim format (kebab-case required for policy matching)
5. **Session 16:** Applied tiered timeout strategy (15s initial, 15s navigation, 10s elements, 5s state checks)

**Result:** Auth E2E scenario passes consistently with realistic timeouts that model production behavior.

### References

- **Implementation:** `tests/Backoffice/Backoffice.E2ETests/Pages/LoginPage.cs`, `DashboardPage.cs`
- **Session Retrospectives:**
  - `docs/planning/milestones/m32.1-session-13-retrospective.md` (authorization policies)
  - `docs/planning/milestones/m32.1-session-16-retrospective.md` (tiered timeout strategy)
- **Triage Plan:** `docs/planning/milestones/m32.1-triage-and-completion-plan.md` (comprehensive timeout analysis)

---

## Test Lifecycle Hooks

### Hooks Overview

```
[BeforeTestRun] (DataHooks, Order=1)
  → Start TestContainers PostgreSQL
  → Start Storefront.Api with Kestrel (stubs + test DB)
  → Start Storefront.Web with Kestrel (pointed at test API)
  → Launch Playwright Chromium browser (headless by default)

[BeforeScenario] (DataHooks, Order=1)
  → Store fixture/playwright/browser in ScenarioContext

[BeforeScenario] (DataHooks, Order=2)
  → Reset all stubs (clear carts, checkouts, addresses)

[BeforeScenario] (DataHooks, Order=3, @checkout tag)
  → Seed standard scenario: Alice's cart + addresses + catalog

[BeforeScenario] (PlaywrightHooks, Order=10)
  → Create browser context (fresh session, isolated cookies/storage)
  → Start Playwright trace (screenshots + DOM snapshots)
  → Create new page, store in ScenarioContext

[Scenario runs]

[AfterScenario] (PlaywrightHooks, Order=10)
  → Close browser context
  → Save trace zip to playwright-traces/ (failure only)

[AfterScenario] (DataHooks, Order=100)
  → Clean Marten event store + documents

[AfterTestRun] (DataHooks, Order=100)
  → Dispose Chromium browser
  → Dispose Playwright
  → Dispose E2ETestFixture (stops Kestrel, stops containers)
```

### DataHooks

```csharp
[Binding]
public sealed class DataHooks
{
    private static E2ETestFixture _fixture = null!;
    private static IPlaywright _playwright = null!;
    private static IBrowser _browser = null!;

    [BeforeTestRun(Order = 1)]
    public static async Task StartInfrastructure()
    {
        _fixture = new E2ETestFixture();
        await _fixture.InitializeAsync();

        _playwright = await Playwright.CreateAsync();

        var headless = !string.Equals(
            Environment.GetEnvironmentVariable("PLAYWRIGHT_HEADLESS"),
            "false",
            StringComparison.OrdinalIgnoreCase);

        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = headless,
            SlowMo = headless ? 0 : 100 // visual debugging aid when headless=false; increase to 300-500ms for slower step-through
        });
    }

    [BeforeScenario("checkout", Order = 3)]
    public async Task SeedCheckoutScenarioData()
    {
        await _fixture.SeedStandardCheckoutScenarioAsync();
    }

    [AfterScenario(Order = 100)]
    public async Task CleanDatabase()
    {
        await _fixture.CleanDatabaseAsync();
    }
}
```

### PlaywrightHooks

```csharp
[Binding]
public sealed class PlaywrightHooks
{
    [BeforeScenario(Order = 10)]
    public async Task CreateBrowserContextAndPage()
    {
        var browser = _scenarioContext.Get<IBrowser>(ScenarioContextKeys.Browser);
        var fixture = _scenarioContext.Get<E2ETestFixture>(ScenarioContextKeys.Fixture);

        // Fresh context per scenario — isolated cookies, localStorage, session
        _browserContext = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = fixture.StorefrontWebBaseUrl
        });

        await _browserContext.Tracing.StartAsync(new TracingStartOptions
        {
            Screenshots = true,
            Snapshots = true,
            Sources = false
        });

        var page = await _browserContext.NewPageAsync();
        _scenarioContext.Set(page, ScenarioContextKeys.Page);
    }

    [AfterScenario(Order = 10)]
    public async Task CloseBrowserContextAndSaveTrace()
    {
        if (_browserContext == null) return;

        if (_scenarioContext.TestError != null)
        {
            var traceDir = Path.Combine(
                Directory.GetCurrentDirectory(),
                "playwright-traces",
                $"{ScenarioTitle}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.zip");

            Directory.CreateDirectory(Path.GetDirectoryName(traceDir)!);

            await _browserContext.Tracing.StopAsync(new TracingStopOptions { Path = traceDir });
        }
        else
        {
            await _browserContext.Tracing.StopAsync(); // discard trace on success
        }

        await _browserContext.CloseAsync();
    }
}
```

## WellKnownTestData Pattern

Never use random IDs in E2E tests. Deterministic constants allow stubs to coordinate without runtime state passing.

```csharp
internal static class WellKnownTestData
{
    internal static class Customers
    {
        public static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
        public const string AliceEmail    = "alice@example.com";
        public const string AlicePassword = "password123";
    }

    internal static class Addresses
    {
        public static readonly Guid AliceHome = Guid.Parse("22222222-2222-2222-2222-222222222222");
        public static readonly Guid AliceWork = Guid.Parse("33333333-3333-3333-3333-333333333333");
        public const string AliceHomeNickname = "Home";
        // ...
    }

    internal static class Checkouts
    {
        public static readonly Guid AliceCheckoutId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    }

    internal static class Orders
    {
        // Pre-seeded in SeedStandardCheckoutScenarioAsync for SignalR scenarios.
        // See Pattern 9 for the setup-via-stub principle.
        public static readonly Guid AliceOrderId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    }

    internal static class ExpectedTotals
    {
        public const decimal Subtotal                    = 69.97m;
        public const decimal StandardShipping            = 5.99m;
        public const decimal TotalWithStandardShipping   = 75.96m;
    }
}
```

## ScenarioContextKeys

Typed string constants prevent silent typo-based `KeyNotFoundException` at runtime.

```csharp
internal static class ScenarioContextKeys
{
    public const string Fixture    = "E2ETestFixture";
    public const string Playwright = "Playwright";
    public const string Browser    = "Browser";
    public const string Page       = "Page";
    public const string CartId     = "CartId";
    public const string CheckoutId = "CheckoutId";
    public const string OrderId    = "OrderId";
}
```

Use them everywhere:

```csharp
// Store
_scenarioContext.Set(orderId, ScenarioContextKeys.OrderId);

// Retrieve
var orderId = _scenarioContext.Get<string>(ScenarioContextKeys.OrderId);
```

## Stub Design

### Structure

Each stub implements the same interface as the real client (`IShoppingClient`, `IOrdersClient`, etc.) and is registered as a singleton so all requests within a scenario share state.

```csharp
public sealed class StubOrdersClient : IOrdersClient
{
    private readonly Dictionary<Guid, CheckoutDto> _checkouts = new();
    private readonly List<OrderDto> _orders = new();

    public void AddCheckout(Guid checkoutId, Guid customerId, params CheckoutItemDto[] items)
        => _checkouts[checkoutId] = new CheckoutDto(checkoutId, customerId, items.ToList(), IsCompleted: false);

    public Task<CheckoutDto> GetCheckoutAsync(Guid checkoutId, CancellationToken ct = default)
    {
        if (_checkouts.TryGetValue(checkoutId, out var checkout))
            return Task.FromResult(checkout);
        throw new HttpRequestException($"Checkout {checkoutId} not found", null, HttpStatusCode.NotFound);
    }

    public Task ProvidePaymentMethodAsync(Guid checkoutId, string token, CancellationToken ct = default)
    {
        // Simulate validation: reject the well-known invalid test token
        if (token == WellKnownTestData.Payment.InvalidToken)
            throw new HttpRequestException("Invalid payment token", null, HttpStatusCode.BadRequest);
        return Task.CompletedTask;
    }

    public void Clear()
    {
        _checkouts.Clear();
        _orders.Clear();
    }
}
```

### Registration Pattern

```csharp
internal static class ServiceCollectionExtensions
{
    public static void RemoveAndReplaceClient<TClient>(
        this IServiceCollection services,
        TClient implementation)
        where TClient : class
    {
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(TClient));
        if (descriptor != null) services.Remove(descriptor);
        services.AddSingleton<TClient>(implementation);
    }
}
```

Called in `StorefrontApiKestrelFactory.ConfigureWebHost()`:

```csharp
services.RemoveAndReplaceClient<IShoppingClient>(stubShoppingClient);
services.RemoveAndReplaceClient<IOrdersClient>(stubOrdersClient);
```

### External HTTP Handlers (Web-Layer Stubs)

When `Storefront.Web` makes an outbound HTTP call internally (e.g., its `/api/auth/login` endpoint proxies to Customer Identity), stub it with a custom `HttpMessageHandler`:

```csharp
internal sealed class StubCustomerIdentityApiHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                customerId = WellKnownTestData.Customers.Alice.ToString(),
                email      = WellKnownTestData.Customers.AliceEmail,
                firstName  = WellKnownTestData.Customers.AliceFirstName,
                lastName   = WellKnownTestData.Customers.AliceLastName
            })
        };
        return Task.FromResult(response);
    }
}

// Registration in StorefrontWebKestrelFactory:
services.Configure<HttpClientFactoryOptions>("CustomerIdentityApi", opts =>
{
    opts.HttpMessageHandlerBuilderActions.Add(b =>
        b.PrimaryHandler = new StubCustomerIdentityApiHandler());
});
```

## Running E2E Tests

```bash
# Run all E2E scenarios (headless, default)
dotnet test tests/Customer\ Experience/Storefront.E2ETests/

# Run specific tag
dotnet test --filter "Category=checkout"

# Run with headed browser for visual debugging
PLAYWRIGHT_HEADLESS=false dotnet test tests/Customer\ Experience/Storefront.E2ETests/

# Skip browser download (if pre-installed)
PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1 dotnet test ...
```

Playwright traces for failed scenarios are saved to `playwright-traces/` relative to the working directory. Open a trace with:

```bash
pwsh playwright.ps1 show-trace playwright-traces/<scenario-name>.zip
```

## CI Diagnostics Workflow for .NET Playwright

CritterSupply's E2E suites use **Microsoft.Playwright for .NET** with Reqnroll hooks. The canonical tracing pattern lives in each suite's `Hooks/PlaywrightHooks.cs`, not in a Node `playwright.config.ts` file.

**What the hooks already capture on failure:**
- Playwright trace zips (`IBrowserContext.Tracing.StartAsync()` / `StopAsync()`)
- Browser console messages
- Page errors
- Failed HTTP requests
- HTTP 4xx/5xx responses

**What CI already does:**
- `.github/workflows/e2e.yml` uploads `**/playwright-traces/**/*.zip` as artifacts on failure
- TRX files are uploaded on every run

**Triage order for flaky or timing-sensitive failures:**
1. Open the saved trace first
2. Check browser console and page-error output from the test logs
3. Check failed requests / 4xx / 5xx responses
4. Verify the page-specific readiness marker actually rendered
5. Only then adjust a timeout or selector

> **Do not** add a Node-style `playwright.config.ts` just to enable tracing for the existing `dotnet test` suites. The repo standard is C# hook-based tracing and CI artifact upload.

## Common Pitfalls

### 1. Calling `CreateDefaultClient()` Before `UseKestrel()`

**Symptom:** `InvalidCastException: KestrelServerImpl cannot be cast to TestServer`

**Cause:** `CreateDefaultClient()` triggers server initialization. Calling it before `UseKestrel(0)` initializes a `TestServer`, which cannot later be replaced by Kestrel.

**Fix:** Always call `UseKestrel(0)` first.

---

### 2. Using `WaitForLoadStateAsync(NetworkIdle)` After Login

**Symptom:** Step definitions that check `page.Url` immediately after login find it still pointing to `/login`, even though authentication succeeded.

**Cause:** Blazor's `NavigationManager.NavigateTo(..., forceLoad: true)` emits a second full-page navigation after the fetch response lands. `NetworkIdle` can settle between the fetch and the reload.

**Fix:** Use `WaitForURLAsync(url => !url.Contains("/login"))` before checking the URL.

---

### 3. `GetByRole(AriaRole.Option, ...)` Times Out on MudSelect

**Symptom:** `TimeoutException` when selecting from a `MudSelect` dropdown.

**Cause:** `GetByRole` matches on the **exact** accessible name. MudBlazor renders the full display text (e.g., `"Home - 123 Main St, Seattle, WA 98101"`), so `{ Name = "Home" }` never matches.

**Fix:** Use `Locator($"[role='option']:has-text('{nickname}')").ClickAsync(new LocatorClickOptions { Timeout = 15_000 })`. Playwright's `Locator.ClickAsync` retries through CSS animation delays automatically. For the fully robust and CI-proven implementation, see Pattern 3 (force-click `.mud-select-input`, wait for `[role='listbox']`, target option by `data-testid`).

---

### 4. `MudSelect` Dropdown Never Opens — `[role='listbox']` Times Out

**Symptom:** `TimeoutException: Timeout 10000ms exceeded — waiting for Locator("[role='listbox']") to be visible` on `SelectAddressByNicknameAsync` across ALL scenarios.

**Root cause (three failure modes, tried in order):**

| Approach | Failure |
|---|---|
| `AddressSelect.ClickAsync()` (outer wrapper) | Bounding-box centre lands on the `<label>` or padding — MudBlazor receives no "open" event; listbox never appears |
| `AddressSelect.Locator(".mud-select-input").ClickAsync()` (no Force) | Playwright's `elementFromPoint()` returns the transparent `.mud-input-mask` overlay; waits 30 s for `.mud-select-input` to be "hittable" — times out |
| `AddressSelect.Locator(".mud-select-input").ClickAsync(Force: true)` | ✅ Bypasses hit-test; synthetic `MouseEvent` dispatched directly to `.mud-select-input` where the `@onclick` handler is — dropdown opens reliably |

**Fix:** Use `Force = true` on the scoped `.mud-select-input` locator:
```csharp
// ❌ Before — outer wrapper unreliable; text-based option selector fragile:
await AddressSelect.ClickAsync();
await page.Locator($"[role='option']:has-text('{nickname}')").ClickAsync();

// ✅ After — Force bypasses overlay hit-test; data-testid is stable:
await AddressSelect.Locator(".mud-select-input").ClickAsync(new LocatorClickOptions { Force = true });
await page.WaitForSelectorAsync("[role='listbox']",
    new PageWaitForSelectorOptions { Timeout = MudSelectListboxTimeoutMs });
await page.Locator($"[data-testid='address-option-{nickname.ToLowerInvariant()}']")
          .ClickAsync(new LocatorClickOptions { Timeout = MudSelectOptionTimeoutMs });
```

See Pattern 3 for full explanation and the two-constant timeout split.

---

### 5. Stubs Return Wrong Data Because Reset Was Skipped

**Symptom:** Scenario B picks up cart data seeded by Scenario A.

**Cause:** Stubs hold in-memory state across scenarios if `Clear()` is not called between them.

**Fix:** Call `_fixture.ClearAllStubs()` in a `[BeforeScenario]` hook with `Order = 2` (runs before data seeding at `Order = 3`).

---

### 6. SignalR Connection Failures in E2E Tests

Two distinct failure modes affect SignalR in E2E tests:

#### 6a. Negotiate Returns 400 (Antiforgery)

**Symptom:** `Status code '400'` with "A valid antiforgery token was not provided…" on the negotiate endpoint.

**Cause:** ASP.NET Core 10+ antiforgery middleware blocks the SignalR negotiate endpoint by default.

**Fix:** Add `.DisableAntiforgery()` to the hub mapping in `Storefront.Api/Program.cs`.

#### 6b. `TypeError: Failed to fetch` on Negotiate (CORS)

**Symptom:** `TypeError: Failed to fetch` in the SignalR client when attempting to connect. The negotiate POST is blocked silently by the browser.

**Cause:** Storefront.Web and Storefront.Api run on different ports in E2E tests (e.g., `localhost:5238` vs `localhost:5237`). Under the browser Same-Origin Policy, **different ports = different origins**, so the negotiate HTTP POST is a cross-origin request. Without CORS headers, the browser blocks the response.

**Fix (belt-and-suspenders — apply both):**

1. Add CORS to `Storefront.Api/Program.cs` so negotiate responses include `Access-Control-Allow-Origin` headers:
```csharp
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});
// ...
app.UseCors(); // before MapWolverineEndpoints + MapHub
```

2. In `signalr-client.js`, set `skipNegotiation: true` to skip the HTTP negotiate POST entirely and connect directly via WebSocket. Safe because transport is already hardcoded to `WebSockets`:
```javascript
.withUrl(url, {
    transport: signalR.HttpTransportType.WebSockets,
    skipNegotiation: true   // bypass the CORS-triggering negotiate POST
})
```

#### 6c. Missing `hubUrl` Argument to `signalrClient.subscribe`

**Symptom:** `Failed to complete negotiation with the server: TypeError: Failed to fetch` but the hub URL in the error reads `undefined?customerId=...`.

**Cause:** The JS `subscribe` function signature is `(customerId, dotNetHelper, hubUrl)`. If the Blazor component calls it with only 2 arguments, `hubUrl` is `undefined` in JavaScript, so the client tries to connect to `"undefined?customerId=..."` which resolves to the Blazor Web server (not the API), producing a 400 or CORS error.

**Fix:** Always pass all three arguments:
```csharp
var apiBaseUrl = Configuration["ApiClients:StorefrontApiUrl"] ?? "http://localhost:5237";
var hubUrl = $"{apiBaseUrl}/hub/storefront";
await JS.InvokeVoidAsync("signalrClient.subscribe", customerId, dotNetHelper, hubUrl);
```

---

### 7. `data-testid` Selectors Not Found

**Symptom:** `TimeoutException` on a locator that should match a visible element.

**Cause:** The Blazor component does not have `data-testid` added, or the attribute is conditionally rendered.

**Fix:** Add `data-testid` to the Blazor component markup. Verify the attribute is present in the rendered DOM using Playwright's `page.Content()` in a failing test.

---

### 8. Ports Collide Between Test Runs

**Symptom:** `AddressAlreadyInUseException` when starting tests on a busy machine.

**Cause:** Using a fixed port instead of `port=0`.

**Fix:** Always pass `port=0` to `UseKestrel()`. The OS allocates a free port; `IServerAddressesFeature` reports the actual address.

---

### 9. Browser UI Used for Setup That Is Not What the Test Is Testing

**Symptom:** A "setup" step (like "Given I have successfully placed an order") drives through a full browser checkout flow, causing the test to fail on MudBlazor timing issues that have nothing to do with what the test is actually checking.

**Cause:** Violating the principle "the browser only touches what the test is testing." Using UI for setup couples the scenario to unrelated components and makes failures harder to diagnose.

**Fix:** Seed state directly in fixtures/stubs, then navigate directly to the relevant page. See Pattern 9 for the complete setup-via-stub approach.

---

### 10. Blazor Server Component State Not Updating After `OnAfterRenderAsync`

**Symptom:** A `data-testid` element that is conditionally rendered (`@if (_flag)`) never appears in the DOM, even though C# code sets `_flag = true`.

**Cause:** In Blazor Server, mutations to component state inside `OnAfterRenderAsync` do NOT automatically schedule a re-render. Unlike `OnInitializedAsync` or event handlers, `OnAfterRenderAsync` is called **after** the render phase — Blazor does not re-render again unless explicitly requested.

**Example:** `OrderConfirmation.razor` calls `SubscribeToSSE()` from `OnAfterRenderAsync`. When `_sseConnected = true` is set after the JavaScript SignalR connect call returns, no re-render occurs, so `@if (_sseConnected)` never makes the `[data-testid='signalr-connected']` element visible.

**Fix:** Always call `StateHasChanged()` after modifying state inside `OnAfterRenderAsync` (or any async continuation that runs outside the Blazor synchronization context):

```csharp
// WRONG — _sseConnected = true is set but no re-render is scheduled
await JS.InvokeVoidAsync("signalrClient.subscribe", ...);
_sseConnected = true;
// UI never updates — Playwright times out waiting for [data-testid='signalr-connected']

// CORRECT — StateHasChanged() schedules a re-render so the UI updates
await JS.InvokeVoidAsync("signalrClient.subscribe", ...);
_sseConnected = true;
StateHasChanged();
```

**Rule of thumb:** If state changes in `OnAfterRenderAsync` or inside a `try/catch` wrapping a JS interop call, add `StateHasChanged()`. If you're unsure, adding it is safe — it is idempotent and just queues one additional render pass.

### Pitfall: NetworkIdle vs. Blazor Rendering Completion

**Symptom:** Test passes locally but fails in CI with "element not visible" or "timeout waiting for element" errors. `WaitForLoadStateAsync(LoadState.NetworkIdle)` completes, but assertions immediately after still fail.

**Root cause:** `NetworkIdle` waits for HTTP requests to settle, but **not** for Blazor to finish rendering the response. In CI environments (slower CPUs, shared runners), the delay between "HTTP complete" and "DOM updated" is more pronounced.

**Example failure:**
```csharp
// Step 1: Clear stub cart data
_fixture.StubShoppingClient.Clear();

// Step 2: Navigate to cart page
await page.GotoAsync("/cart");
await page.WaitForLoadStateAsync(LoadState.NetworkIdle); // ✅ HTTP complete

// Step 3: Check for empty cart message
var isVisible = await page.GetByText("Your cart is empty.").IsVisibleAsync();
// ❌ Returns false in CI — Blazor hasn't rendered the empty state yet
```

**Why this happens:**
1. `WaitForLoadStateAsync(LoadState.NetworkIdle)` ensures no more HTTP requests are in-flight
2. But Blazor's `OnAfterRenderAsync` might still be executing (e.g., calling `LoadCart()`, updating `_cartView`, setting `_isLoading = false`)
3. The component hasn't called `StateHasChanged()` or completed its render cycle yet
4. Test checks `IsVisibleAsync()` immediately → returns `false` because DOM hasn't updated

**Fix:** Use `WaitForAsync()` with a timeout instead of `IsVisibleAsync()` when checking for elements that depend on async component lifecycle:

```csharp
// WRONG — immediate check, no waiting for render
public async Task<bool> IsEmptyCartMessageVisibleAsync()
{
    return await EmptyCartMessage.IsVisibleAsync();
}

// CORRECT — wait for element to appear in DOM before checking visibility
public async Task<bool> IsEmptyCartMessageVisibleAsync()
{
    try
    {
        await EmptyCartMessage.WaitForAsync(new() { Timeout = 5000 });
        return await EmptyCartMessage.IsVisibleAsync();
    }
    catch (TimeoutException)
    {
        return false;
    }
}
```

**When to use `WaitForAsync()`:**
- After navigation to a page with async data loading (`OnAfterRenderAsync`, `OnInitializedAsync`)
- After stub data changes that require component re-render
- After actions that trigger SignalR/SSE updates
- In POM methods that check for conditionally-rendered elements (empty states, error messages, success notifications)

**When `IsVisibleAsync()` is safe:**
- Checking for elements that are always present (navigation bar, page title)
- After explicitly waiting for another element first (e.g., after `WaitForURLAsync()` + waiting for a form to appear, checking if submit button is enabled)
- In assertions where failure is expected (negative tests)

**CI-specific considerations:**
- CI runners often have slower CPUs and higher latency
- Default Playwright timeouts (30s) are usually sufficient **after bootstrap**, but initial WASM cold start may need a 45-60 second readiness window in CI
- Use `PLAYWRIGHT_HEADLESS=false` locally to debug timing issues — if the element appears *after* the error message, you have a wait strategy problem

## Checklist for New E2E Scenarios

Use this checklist when adding a new Gherkin scenario to `checkout-flow.feature` or a new feature file.

### Before Writing the Scenario

- [ ] Is this the right test level? Could an Alba integration test cover this behavior?
- [ ] Does the scenario require a real browser DOM or real HTTP port? (If not, use Alba)
- [ ] Is the feature file in `docs/features/<bc>/` first?
- [ ] For setup steps ("Given an order exists"): does the setup need browser UI, or can it be done via stub + direct navigation? (Pattern 9)
- [ ] If testing auth/redirect behavior: is this in a feature file without a "login" Background that would pre-authenticate the user?

### Gherkin Scenario

- [ ] Tag with `@checkout` if it needs the standard seed data
- [ ] Tag with `@signalr` if it requires real-time hub delivery
- [ ] Tag with `@ignore` if not yet implemented and must be skipped by Reqnroll
- [ ] Steps are user-centric, not implementation-centric

### Step Definitions

- [ ] New steps in `CheckoutFlowStepDefinitions.cs` (or a new `*StepDefinitions.cs` for a new feature)
- [ ] Steps retrieve `IPage` from `ScenarioContext` using `ScenarioContextKeys.Page`
- [ ] Steps delegate to a POM method — no raw Playwright locators in step definitions
- [ ] State shared between steps stored with `ScenarioContextKeys` constants

### Page Object Model

- [ ] New page gets its own `*Page.cs` in `Pages/`
- [ ] All locators use `data-testid` attributes (or ARIA roles/labels for login inputs)
- [ ] Waiting strategies match the component's rendering behavior (e.g., MudSelect popup wait)
- [ ] POM exposes logical operations, not raw Playwright API calls

### Test Data

- [ ] Any new IDs added to `WellKnownTestData` with a `Guid.Parse("N…N")` constant
- [ ] Stub seeding for the new scenario added to `E2ETestFixture` (or a new seed method)
- [ ] `StubShoppingClient.SetCheckoutId()` called if the scenario crosses the cart → checkout boundary
- [ ] New `ScenarioContextKeys` constant added if new cross-step state is needed

### Stubs

- [ ] Stub returns HTTP 4xx for known invalid inputs (e.g., `WellKnownTestData.Payment.InvalidToken`)
- [ ] Stub's `Clear()` method resets all new state fields
- [ ] `E2ETestFixture.ClearAllStubs()` calls the new `Clear()` method

### Blazor Components

- [ ] New interactive elements have `data-testid` attributes added
- [ ] `data-testid` value follows the pattern: `kebab-case`, noun before verb (e.g., `btn-place-order`)
- [ ] If state is modified in `OnAfterRenderAsync` or a JS interop callback, `StateHasChanged()` is called to schedule re-render

### Verification

- [ ] Scenario passes locally with `PLAYWRIGHT_HEADLESS=false` for visual confirmation
- [ ] Scenario passes headless
- [ ] Playwright trace reviewed (if the scenario failed during development)

## References

- **Implementation:** `tests/Customer Experience/Storefront.E2ETests/`
  - `E2ETestFixture.cs` — Kestrel factory setup, stub wiring, seed data
  - `Hooks/DataHooks.cs` — `[BeforeTestRun]`, `[BeforeScenario]`, `[AfterScenario]`
  - `Hooks/PlaywrightHooks.cs` — browser context lifecycle + tracing
  - `Pages/CheckoutPage.cs` — 4-step MudStepper POM
  - `Pages/LoginPage.cs`, `CartPage.cs`, `OrderConfirmationPage.cs`
  - `Stubs/StubShoppingClient.cs`, `StubOrdersClient.cs`, `StubCatalogClient.cs`
  - `WellKnownTestData.cs` — deterministic test constants
  - `ScenarioContextKeys.cs` — typed ScenarioContext keys
- **Feature file:** `tests/Customer Experience/Storefront.E2ETests/Features/checkout-flow.feature`
- **Gherkin specification:** `docs/features/customer-experience/checkout-flow.feature`
- [Microsoft Playwright for .NET](https://playwright.dev/dotnet/)
- [reqnroll-bdd-testing.md](./reqnroll-bdd-testing.md) — Reqnroll step definition and hook patterns
- [testcontainers-integration-tests.md](./testcontainers-integration-tests.md) — TestContainers lifecycle and fixture patterns
- [critterstack-testing-patterns.md](./critterstack-testing-patterns.md) — Alba integration test patterns
- [bff-realtime-patterns.md](./bff-realtime-patterns.md) — Storefront BFF architecture and SignalR hub design

---

## Vendor Portal E2E Architecture (Cycle 23)

The Vendor Portal uses a different architecture than the Storefront because it's **Blazor WASM** (not Blazor Server):

### 3-Server Fixture

```
Playwright Browser (Chromium, headless)
     │
     ▼
VendorPortal.Web (WASM static files via thin ASP.NET host, port=0)
     │ (cross-origin HTTP + WebSocket)
     ├─────────────────────────┐
     ▼                         ▼
VendorPortal.Api            VendorIdentity.Api
(real Kestrel, port=0)      (real Kestrel, port=0)
├── Marten (events/docs)    ├── EF Core (users/tenants)
├── SignalR hub              ├── JWT issuance
└── JWT validation           └── Auto-seeded demo accounts
     │                         │
     └── Shared PostgreSQL ────┘
         (TestContainers)
```

### Key Differences from Storefront E2E

| Concern | Storefront (Blazor Server) | Vendor Portal (Blazor WASM) |
|---------|---------------------------|----------------------------|
| **WebApplicationFactory** | Both Storefront.Web + Storefront.Api use WAF | Only API projects use WAF; WASM uses custom static host |
| **Auth** | Session cookie (stubbed handler) | Real JWT from real VendorIdentity.Api |
| **WASM hydration** | Instant (SSR) | 5–30s cold start (.NET runtime download) |
| **Configuration injection** | `IConfiguration` in WAF | Intercept `/appsettings.json` HTTP fetch |
| **CORS** | Same-origin (Blazor Server) | Cross-origin (WASM → API) |
| **Anchor type** | `StorefrontHub`, `StorefrontWebMarker` | `VendorPortalHub`, `VendorLoginEndpoint` |
| **Downstream stubs** | All 4 BC clients stubbed | No stubs — real APIs with real DB |

### WASM Static File Host Pattern

```csharp
// Blazor WASM apps fetch wwwroot/appsettings.json via HTTP at boot.
// Intercept this to inject test API URLs:
app.MapGet("/appsettings.json", () => Results.Json(new
{
    ApiClients = new
    {
        VendorIdentityApiUrl = identityApiUrl,  // random port from test
        VendorPortalApiUrl = portalApiUrl        // random port from test
    }
}));

// Serve compiled WASM files + SPA fallback
app.UseStaticFiles(new StaticFileOptions { FileProvider = wasmRoot, ServeUnknownFileTypes = true });
app.MapFallbackToFile("index.html", staticOptions);
```

### Two-Program-Class Problem

When referencing VendorPortal.Api AND VendorIdentity.Api, both have a top-level `public partial class Program { }`. The compiler can't disambiguate `Program` as a type parameter.

**Solution:** Use domain-specific types as `WebApplicationFactory<T>` anchors:
- `WebApplicationFactory<VendorPortal.Api.Hubs.VendorPortalHub>` — for VendorPortal.Api
- `WebApplicationFactory<VendorIdentity.Api.Auth.VendorLoginEndpoint>` — for VendorIdentity.Api

### WASM Hydration Wait

Blazor WASM requires downloading the .NET runtime and all assemblies before any Blazor component renders. This takes 5–30 seconds on first load:

```csharp
// Wait for WASM to hydrate — must wait for a real Blazor element, not just NetworkIdle
await page.WaitForSelectorAsync("[data-testid='login-btn']", new PageWaitForSelectorOptions
{
    Timeout = 60000 // CI cold start + MudBlazor initialization can exceed 30s
});
```

Vendor Portal page objects deliberately avoid calling `WaitForLoadStateAsync(NetworkIdle)` inside per-field helpers once the caller has already verified hydration. In the real fixture, background Marten/Wolverine/SignalR activity can keep `NetworkIdle` noisy even after the form is ready for use.

### SignalR Hub Message Injection

For testing real-time features, inject messages via `IHubContext` directly:

```csharp
var hubContext = fixture.PortalApiHost.Services
    .GetRequiredService<IHubContext<VendorPortalHub>>();

// Wolverine CloudEvents envelope format
var envelope = new { type = "LowStockAlertRaised", data = new { sku = "DOG-BOWL-01" } };

await hubContext.Clients
    .Group($"vendor:{tenantId}")
    .SendAsync("ReceiveMessage", JsonSerializer.SerializeToElement(envelope));
```

### Reference Files

- **Fixture:** `tests/Vendor Portal/VendorPortal.E2ETests/E2ETestFixture.cs`
- **Hooks:** `DataHooks.cs`, `PlaywrightHooks.cs`
- **Page Objects:** `VendorLoginPage.cs`, `VendorDashboardPage.cs`, `ChangeRequestsPage.cs`, `SubmitChangeRequestPage.cs`
- **Feature Files:** `vendor-auth.feature`, `vendor-dashboard.feature`, `vendor-change-requests.feature`
- **Test Data:** `WellKnownVendorTestData.cs` — matches VendorIdentitySeedData demo accounts
- **Cycle Plan:** `docs/planning/cycles/cycle-23-vendor-portal-e2e-testing.md`

---

## WASM-Specific Testing Patterns (M32.1)

Blazor WASM E2E tests require different timing strategies than Blazor Server tests due to cold start delays, MudBlazor initialization, JWT auth propagation, and SignalR connection establishment. The patterns below are derived from Backoffice WASM E2E testing (M32.1 Sessions 12-16).

### Tiered Timeout Strategy

**Problem:** Blazor WASM cold start (5-30s) + MudBlazor initialization (2-5s) + JWT auth (1-3s) + SignalR connection (1-5s) compound to create 30-40s total delay. Fixed 30s timeout is too aggressive for initial load but wasteful for fast operations.

**Solution:** Use operation-specific timeouts optimized for actual timing behavior:

| Operation | Timeout | Rationale |
|-----------|---------|-----------|
| **Initial page load** | 45-60s + hydration check | CI cold start, WASM download, .NET runtime, app assemblies, MudBlazor initialization |
| **Authenticated navigation** | 15s | Auth state propagation (1-3s) + component re-render |
| **SignalR connection** | 15s | JWT must complete first + retry attempts |
| **Element visibility** | 10s | Standard Playwright wait (sufficient after hydration) |
| **State checks** | 5s | DOM polling for real-time indicators |

Use the 45-60 second ceiling only for **first-load bootstrap**. Keep the faster waits short so real regressions still fail quickly.

**Pattern:**

```csharp
// LoginPage.cs — Initial page load with MudBlazor hydration check
public async Task NavigateAsync()
{
    await _page.GotoAsync(_baseUrl);

    // Step 1: Wait for network idle
    await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);

    // Step 2: Wait for MudBlazor initialization (CRITICAL for WASM)
    await _page.WaitForSelectorAsync(".mud-dialog-provider", new() { Timeout = 15_000 });

    // Step 3: Wait for specific form element
    await EmailInput.WaitForAsync(new() { Timeout = 60_000 });
}

// LoginPage.cs — Post-login navigation with auth state propagation
public async Task LoginAsync(string email, string password)
{
    await EmailInput.FillAsync(email);
    await PasswordInput.FillAsync(password);
    await LoginButton.ClickAsync();

    // Auth state propagation needs 15s (not 10s)
    await _page.WaitForURLAsync(url => url.Contains("/dashboard"), new() { Timeout = 15_000 });
}

// DashboardPage.cs — SignalR connection check
public async Task WaitForSignalRConnectionAsync()
{
    // SignalR depends on JWT auth completing first
    await RealtimeConnectedIndicator.WaitForAsync(new() { Timeout = 15_000 });
}

// DashboardPage.cs — Fast element checks after hydration
public async Task<bool> IsKpiCardVisibleAsync(string kpiName)
{
    var locator = _page.Locator($"[data-testid='kpi-{kpiName}']");
    await locator.WaitForAsync(new() { Timeout = 10_000 }); // Standard timeout
    return await locator.IsVisibleAsync();
}
```

**Anti-Patterns:**

```csharp
// ❌ WRONG: Fixed 30s timeout everywhere (wasteful for fast operations)
await EmailInput.WaitForAsync(new() { Timeout = 30_000 });
await LoginButton.ClickAsync();
await _page.WaitForURLAsync(url => url.Contains("/dashboard"), new() { Timeout = 30_000 });

// ❌ WRONG: Too aggressive for WASM initial load
await EmailInput.WaitForAsync(new() { Timeout = 5_000 }); // Times out during cold start

// ❌ WRONG: Using NetworkIdle alone without MudBlazor hydration check
await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
// Components not ready yet — clicking elements before MudBlazor event handlers attach
```

---

### Explicit Hydration Detection

**Problem:** `NetworkIdle` doesn't guarantee MudBlazor components are ready. Tests were clicking elements before MudBlazor event handlers attached, causing flaky failures.

**Solution:** Check for framework-specific markers (MudBlazor provider classes, SignalR connection state, auth state) in addition to `NetworkIdle`.

**Readiness ladder for Blazor WASM + MudBlazor pages:**
1. Serve the published WASM output (`index.html` + `_framework`)
2. Navigate to the page
3. Wait for network / document load to settle
4. Wait for MudBlazor provider markers
5. Wait for the first page-specific interactive control
6. After login, wait for auth/navigation to complete
7. On real-time pages, wait for SignalR connected state before asserting business UI

**MudBlazor Hydration Check:**

```csharp
// Wait for MudBlazor initialization (check for MudBlazor provider classes)
await _page.WaitForSelectorAsync(".mud-dialog-provider",
    new() { State = WaitForSelectorState.Attached, Timeout = 15_000 });
```

**Why This Works:** MudBlazor v9+ creates `.mud-dialog-provider` and `.mud-snackbar-provider` containers during initialization. These providers are the last step of MudBlazor setup. If provider exists, MudBlazor is ready.

**Multi-Layer Verification Pattern:**

```csharp
public async Task WaitForPageReadyAsync()
{
    // Layer 1: Wait for network idle (basic HTML loaded)
    await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);

    // Layer 2: Wait for framework initialization (MudBlazor ready)
    await _page.WaitForSelectorAsync(".mud-dialog-provider", new() { Timeout = 15_000 });

    // Layer 3: Wait for auth state (if authenticated page)
    if (_requiresAuth)
    {
        await _page.WaitForURLAsync(url => url.Contains("/dashboard"), new() { Timeout = 15_000 });
    }

    // Layer 4: Wait for SignalR connection (if real-time page)
    if (_requiresSignalR)
    {
        await _page.Locator("[data-testid='realtime-connected']")
            .WaitForAsync(new() { Timeout = 15_000 });
    }
}
```

**Anti-Pattern:**

```csharp
// ❌ WRONG: Skipping hydration checks causes race conditions
await _page.GotoAsync(_baseUrl);
await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
// Immediately clicking — MudBlazor not ready yet!
await _page.Locator("[data-testid='login-submit']").ClickAsync();
```

---

### Auth State Propagation in WASM

**Problem:** After successful login API call, Blazor WASM takes 1-3s to:
1. Update `BackofficeAuthState` (or equivalent auth service)
2. Fire `OnChange` event
3. Call `NotifyAuthenticationStateChanged()`
4. Re-render protected components
5. Execute `NavigationManager.NavigateTo("/dashboard")`

**Impact:** 10s timeout for dashboard navigation was insufficient. 15s is more reliable.

**Pattern:**

```csharp
public async Task LoginAsync(string email, string password)
{
    await EmailInput.FillAsync(email);
    await PasswordInput.FillAsync(password);
    await LoginButton.ClickAsync();

    // Wait for auth state propagation + navigation (15s for WASM, not 10s)
    await _page.WaitForURLAsync(url => url.Contains("/dashboard"), new() { Timeout = 15_000 });

    // Optional: Verify auth state reflected in UI
    await _page.Locator("[data-testid='logout-button']").WaitForAsync(new() { Timeout = 5_000 });
}
```

**Why 15s Not 10s:**
- Login API call: 100-500ms
- Auth state update: 500-1000ms (in-memory state mutation)
- `NotifyAuthenticationStateChanged()` propagation: 500-1000ms (Blazor change detection)
- Protected route check: 100-500ms (auth guard evaluation)
- Component re-render: 500-1000ms (Blazor rendering pipeline)
- Navigation execution: 100-500ms (URL update)

**Total:** 1,800-4,500ms typical, up to 8-10s in slow CI environments. 15s provides safety margin.

---

### SignalR Connection Dependency on JWT

**Problem:** SignalR hub uses `AccessTokenProvider` delegate to get JWT from auth state. If auth state isn't ready, connection fails silently and retries.

**Impact:** SignalR connection timeout must be >= auth propagation timeout. 10s was insufficient because auth state took 1-3s to propagate, leaving only 7-9s for SignalR connection + retries.

**Solution:** Increase SignalR connection timeout to 15s (same as auth propagation timeout).

**Pattern:**

```csharp
public async Task WaitForDashboardReadyAsync()
{
    // Step 1: Wait for auth state propagation + navigation
    await _page.WaitForURLAsync(url => url.Contains("/dashboard"), new() { Timeout = 15_000 });

    // Step 2: Wait for SignalR connection (15s to allow for retry attempts)
    await _page.Locator("[data-testid='realtime-connected']")
        .WaitForAsync(new() { Timeout = 15_000 });

    // Step 3: Wait for initial data load (KPI cards, etc.)
    await _page.Locator("[data-testid='kpi-total-orders']")
        .WaitForAsync(new() { Timeout = 10_000 });
}
```

**Why SignalR Needs 15s:**
- JWT retrieval from auth state: 500-1000ms (waits for auth state ready)
- SignalR connection attempt: 2-5s (WebSocket handshake + JWT validation)
- Retry on failure: 2-5s (if JWT not ready on first attempt)
- Hub connection established: 500-1000ms (connection confirmation)

**Total:** 5-12s typical. 15s allows for 2-3 retry attempts if JWT retrieval is slow.

**SignalR Client Pattern (Blazor WASM):**

```csharp
// Program.cs or hub service
builder.Services.AddSingleton<IBackofficeHubService>(sp =>
{
    var authState = sp.GetRequiredService<BackofficeAuthState>();
    var connection = new HubConnectionBuilder()
        .WithUrl("http://localhost:5243/hub/backoffice", opts =>
        {
            // CRITICAL: AccessTokenProvider must wait for auth state ready
            opts.AccessTokenProvider = async () =>
            {
                // Wait for JWT to be available (auth state may not be ready immediately)
                var maxWait = TimeSpan.FromSeconds(5);
                var elapsed = TimeSpan.Zero;
                var pollInterval = TimeSpan.FromMilliseconds(100);

                while (string.IsNullOrEmpty(authState.AccessToken) && elapsed < maxWait)
                {
                    await Task.Delay(pollInterval);
                    elapsed += pollInterval;
                }

                return authState.AccessToken;
            };
        })
        .WithAutomaticReconnect()
        .Build();

    return new BackofficeHubService(connection);
});
```

---

### Test-ID Naming Conventions

**Problem:** Session 14 found 17 test-id mismatches between Page Object Models and Razor components. Inconsistent naming (`kpi-active-orders` vs `kpi-total-orders`, `customer-search-btn` vs `nav-customer-service`) caused test failures even though functionality worked.

**Solution:** Document test-id conventions with clear patterns, examples, and anti-patterns.

**Convention Table:**

| Element Type | Pattern | Examples | Anti-Patterns |
|--------------|---------|----------|---------------|
| **KPI Cards** | `kpi-{metric-name}` | `kpi-total-orders`, `kpi-revenue`, `kpi-pending-returns` | ❌ `kpi-active-orders` (ambiguous)<br>❌ `kpi-card-orders` (redundant) |
| **KPI Values (nested)** | `kpi-value` | Always nested within KPI card | ❌ `kpi-total-orders-value` (redundant)<br>❌ `value` (too generic) |
| **Navigation Links** | `nav-{destination}` | `nav-customer-service`, `nav-operations`, `nav-pricing` | ❌ `customer-search-btn` (component name)<br>❌ `nav-cs` (abbreviation) |
| **Form Inputs** | `{form}-{field}` | `login-email`, `login-password`, `search-query` | ❌ `email-input` (presentational)<br>❌ `txt-email` (Hungarian notation) |
| **Form Buttons** | `{form}-{action}` | `login-submit`, `logout-button`, `search-submit` | ❌ `submit-btn` (generic)<br>❌ `btn-1` (positional) |
| **Real-time Indicators** | `realtime-{state}` | `realtime-connected`, `realtime-disconnected`, `realtime-reconnecting` | ❌ `hub-status` (implementation detail)<br>❌ `connection-indicator` (verbose) |
| **Data Tables** | `table-{entity-plural}` | `table-orders`, `table-customers`, `table-products` | ❌ `grid-orders` (component name)<br>❌ `orders-table` (inconsistent order) |
| **Table Rows** | `row-{entity}-{id}` | `row-order-12345`, `row-customer-67890` | ❌ `order-row-12345` (inconsistent)<br>❌ `table-row-0` (positional index) |
| **Action Buttons** | `{entity}-{action}` | `order-cancel`, `product-edit`, `customer-view` | ❌ `cancel-order` (verb-first)<br>❌ `btn-cancel-order` (redundant prefix) |

**Key Principles:**

1. **Semantic names:** Describe what element represents, not how it looks
2. **Kebab-case:** Aligns with HTML attribute conventions (`data-testid="login-email"`)
3. **Hierarchical structure:** Parent-child relationships (KPI card contains `kpi-value`)
4. **Stable identifiers:** Don't change when UI styling or ordering changes
5. **Noun before verb:** `order-cancel` (not `cancel-order`)
6. **Avoid implementation details:** Use semantic names (`nav-customer-service`) not component names (`customer-search-btn`)

**Razor Component Example:**

```razor
<!-- ✅ GOOD: Semantic, hierarchical test-ids -->
<MudCard data-testid="kpi-total-orders">
    <MudCardContent>
        <MudText Typo="Typo.h6">Total Orders</MudText>
        <MudText Typo="Typo.h3" data-testid="kpi-value">@TotalOrders</MudText>
    </MudCardContent>
</MudCard>

<MudNavLink Href="/customer-service" data-testid="nav-customer-service">
    Customer Service
</MudNavLink>

<!-- ❌ BAD: Presentational, component-specific test-ids -->
<MudCard data-testid="kpi-card-orders">
    <MudCardContent>
        <MudText Typo="Typo.h6">Total Orders</MudText>
        <MudText Typo="Typo.h3" data-testid="kpi-total-orders-value">@TotalOrders</MudText>
    </MudCardContent>
</MudCard>

<MudNavLink Href="/customer-service" data-testid="customer-search-btn">
    Customer Service
</MudNavLink>
```

**Page Object Model Example:**

```csharp
// Page Object Model defines the contract (expected test-ids)
public class DashboardPage
{
    private readonly IPage _page;

    // KPI cards
    private ILocator TotalOrdersCard => _page.Locator("[data-testid='kpi-total-orders']");
    private ILocator RevenueCard => _page.Locator("[data-testid='kpi-revenue']");
    private ILocator PendingReturnsCard => _page.Locator("[data-testid='kpi-pending-returns']");

    // KPI values (nested within cards)
    public async Task<string> GetTotalOrdersAsync()
        => await TotalOrdersCard.Locator("[data-testid='kpi-value']").TextContentAsync();

    // Navigation links
    private ILocator CustomerServiceLink => _page.Locator("[data-testid='nav-customer-service']");
    private ILocator OperationsLink => _page.Locator("[data-testid='nav-operations']");

    // Real-time indicator
    private ILocator RealtimeConnectedIndicator => _page.Locator("[data-testid='realtime-connected']");
}
```

---

### Page Object Model Best Practices

**Problem:** Session 14 found test-id mismatches because Dashboard.razor was written first with arbitrary test-ids, then DashboardPage.cs expected different test-ids. Tests failed due to contract mismatch (not functional bugs).

**Better Approach:** Write Page Object Model BEFORE Razor component to define test-id contract upfront.

**Recommended Workflow:**

1. **Write Gherkin `.feature` file** (user stories, scenarios)
2. **Write Page Object Model** with expected test-ids (defines contract)
3. **Write Razor component** implementing those test-ids (fulfills contract)
4. **Write step definitions** using Page Object Model

**Why This Works:**
- POM defines the contract (expected test-ids)
- Razor component fulfills the contract (implements those test-ids)
- If component is written first, POM must adapt to component's arbitrary test-ids
- Contract-first prevents mismatches during implementation

**Example Workflow:**

```gherkin
# Step 1: Write Gherkin feature file
Feature: Dashboard KPI Display
  As a system administrator
  I want to view real-time KPI metrics
  So that I can monitor system health

  Scenario: View total orders KPI
    Given I am logged in as a system administrator
    When I navigate to the dashboard
    Then I should see the total orders KPI card
    And the total orders count should be greater than zero
```

```csharp
// Step 2: Write Page Object Model (defines test-id contract)
public class DashboardPage
{
    private readonly IPage _page;

    // Contract: Razor component MUST have data-testid="kpi-total-orders"
    private ILocator TotalOrdersCard => _page.Locator("[data-testid='kpi-total-orders']");

    // Contract: KPI value MUST be nested with data-testid="kpi-value"
    public async Task<int> GetTotalOrdersAsync()
    {
        var text = await TotalOrdersCard.Locator("[data-testid='kpi-value']").TextContentAsync();
        return int.Parse(text);
    }

    public async Task<bool> IsTotalOrdersCardVisibleAsync()
        => await TotalOrdersCard.IsVisibleAsync();
}
```

```razor
<!-- Step 3: Write Razor component (implements contract) -->
<MudCard data-testid="kpi-total-orders">
    <MudCardContent>
        <MudText Typo="Typo.h6">Total Orders</MudText>
        <MudText Typo="Typo.h3" data-testid="kpi-value">@TotalOrders</MudText>
    </MudCardContent>
</MudCard>

@code {
    [Parameter] public int TotalOrders { get; set; }
}
```

```csharp
// Step 4: Write step definitions (uses POM contract)
[When(@"I navigate to the dashboard")]
public async Task WhenINavigateToDashboard()
{
    var page = _scenarioContext.Get<IPage>(ScenarioContextKeys.Page);
    var dashboardPage = new DashboardPage(page);
    await dashboardPage.NavigateAsync();
}

[Then(@"I should see the total orders KPI card")]
public async Task ThenIShouldSeeTotalOrdersKpiCard()
{
    var page = _scenarioContext.Get<IPage>(ScenarioContextKeys.Page);
    var dashboardPage = new DashboardPage(page);
    (await dashboardPage.IsTotalOrdersCardVisibleAsync()).ShouldBeTrue();
}

[Then(@"the total orders count should be greater than zero")]
public async Task ThenTotalOrdersCountShouldBeGreaterThanZero()
{
    var page = _scenarioContext.Get<IPage>(ScenarioContextKeys.Page);
    var dashboardPage = new DashboardPage(page);
    var count = await dashboardPage.GetTotalOrdersAsync();
    count.ShouldBeGreaterThan(0);
}
```

**Anti-Pattern Workflow (Component-First):**

```razor
<!-- ❌ WRONG: Write component first with arbitrary test-ids -->
<MudCard data-testid="dashboard-card-orders">
    <MudCardContent>
        <MudText Typo="Typo.h6">Total Orders</MudText>
        <MudText Typo="Typo.h3" data-testid="order-count">@TotalOrders</MudText>
    </MudCardContent>
</MudCard>
```

```csharp
// ❌ WRONG: POM expects different test-ids (mismatch!)
public class DashboardPage
{
    // Test expects "kpi-total-orders" but component has "dashboard-card-orders"
    private ILocator TotalOrdersCard => _page.Locator("[data-testid='kpi-total-orders']");

    // Test expects "kpi-value" but component has "order-count"
    public async Task<int> GetTotalOrdersAsync()
    {
        var text = await TotalOrdersCard.Locator("[data-testid='kpi-value']").TextContentAsync();
        return int.Parse(text);
    }
}

// Result: Test fails with "Locator not found" (not a functional bug, just contract mismatch)
```

---

### MudBlazor v9+ Type Parameters

**Problem:** MudBlazor v9+ requires explicit type parameters even for non-data-bound lists. Build errors: "The type of component 'MudList' cannot be inferred..."

**Root Cause:** MudBlazor v9+ is generic-first. Type inference fails for non-data-bound lists.

**Fix:**

```razor
<!-- ❌ WRONG (v8 syntax) -->
<MudList>
    <MudListItem Icon="@Icons.Material.Filled.Dashboard">Dashboard</MudListItem>
    <MudListItem Icon="@Icons.Material.Filled.Search">Customer Search</MudListItem>
</MudList>

<!-- ✅ RIGHT (v9+ syntax) -->
<MudList T="string">
    <MudListItem T="string" Icon="@Icons.Material.Filled.Dashboard">Dashboard</MudListItem>
    <MudListItem T="string" Icon="@Icons.Material.Filled.Search">Customer Search</MudListItem>
</MudList>
```

**Impact:** All components using `MudList`, `MudListItem`, `MudTable`, `MudSelect`, and other generic MudBlazor components must specify `T` parameter.

**When to Use What Type:**
- Navigation lists (non-data-bound): `T="string"`
- Data tables: `T="ProductDto"`, `T="OrderDto"`, etc.
- Dropdowns: `T="string"` (for string values) or `T="ProductDto"` (for object binding)

**Common MudBlazor Components Requiring Type Parameters:**

| Component | Type Parameter | Example |
|-----------|----------------|---------|
| `MudList` | `T="string"` | Navigation menus |
| `MudListItem` | `T="string"` | Individual nav items |
| `MudTable` | `T="OrderDto"` | Data tables |
| `MudSelect` | `T="string"` or `T="ProductDto"` | Dropdowns |
| `MudAutocomplete` | `T="string"` or `T="CustomerDto"` | Autocomplete |
| `MudChipSet` | `T="string"` | Chip collections |

---

### Reference Implementations

**Backoffice E2E Tests (M32.1):**
- **Fixture:** `tests/Backoffice/Backoffice.E2ETests/E2ETestFixture.cs` (3-server WASM pattern)
- **Page Objects:** `LoginPage.cs` (tiered timeout strategy), `DashboardPage.cs` (test-id conventions)
- **Features:** `Authentication.feature`, `CustomerService.feature`, `OperationsAlerts.feature`
- **Test Data:** `WellKnownBackofficeTestData.cs`

**Vendor Portal E2E Tests (Cycle 23):**
- **Fixture:** `tests/Vendor Portal/VendorPortal.E2ETests/E2ETestFixture.cs`
- **Page Objects:** `VendorLoginPage.cs`, `VendorDashboardPage.cs`
- **Features:** `vendor-auth.feature`, `vendor-dashboard.feature`

**Milestone Retrospectives:**
- **M32.1 Session 16:** Tiered timeout strategy, hydration detection, auth/SignalR timing
- **M32.1 Session 14-15:** Test-ID naming conventions, POM-first workflow
- **M32.1 Session 6:** MudBlazor v9+ type parameters
- **M32.2 Sessions 4-6:** Session expiry, authorization, data freshness E2E patterns

---

## M32.2: Session Expiry & Authorization E2E Patterns

**Context:** M32.2 added UX hardening for Backoffice WASM app (8 features: session expiry recovery, authorization/RBAC, alert acknowledgment 409/401 handling, data freshness indicators). E2E tests validate these cross-cutting concerns.

---

### Pattern: SimulateSessionExpired for Stub Clients

**Problem:** Need to test session expiry (401 Unauthorized) responses across all API clients without spinning up real auth infrastructure.

**Solution:** Add `SimulateSessionExpired` bool property to all stub clients. When true, throw `HttpRequestException` with `HttpStatusCode.Unauthorized`.

**Implementation:**

```csharp
// tests/Backoffice/Backoffice.E2ETests/Stubs/StubInventoryClient.cs
public sealed class StubInventoryClient : IInventoryClient
{
    /// <summary>
    /// When true, all API methods will throw HttpRequestException with 401 Unauthorized.
    /// Used by SessionExpirySteps to simulate session expiry.
    /// </summary>
    public bool SimulateSessionExpired { get; set; }

    public Task<StockLevelDto?> GetStockLevelAsync(string sku, CancellationToken ct = default)
    {
        if (SimulateSessionExpired)
            throw new HttpRequestException("Session expired", null, HttpStatusCode.Unauthorized);

        return Task.FromResult(_stockLevels.GetValueOrDefault(sku));
    }

    public Task<IReadOnlyList<LowStockDto>> GetLowStockAsync(
        int? threshold = null,
        CancellationToken ct = default)
    {
        if (SimulateSessionExpired)
            throw new HttpRequestException("Session expired", null, HttpStatusCode.Unauthorized);

        // ... rest of method
    }
}
```

**Step Definition Usage:**

```csharp
// tests/Backoffice/Backoffice.E2ETests/StepDefinitions/SessionExpirySteps.cs
[When(@"my session expires")]
public void WhenMySessionExpires()
{
    // Mark session as expired — stub will return 401 for next API call
    Fixture.StubInventoryClient.SimulateSessionExpired = true;
    Fixture.StubOrdersClient.SimulateSessionExpired = true;
    Fixture.StubCustomerIdentityClient.SimulateSessionExpired = true;
}

[When(@"I trigger a data refresh")]
public async Task WhenITriggerADataRefresh()
{
    var dashboardPage = new DashboardPage(Page, Fixture.WasmBaseUrl);
    await dashboardPage.ClickRefreshButtonInBannerAsync();
}

[Then(@"I should see the session expired modal")]
public async Task ThenIShouldSeeTheSessionExpiredModal()
{
    var sessionExpiredPage = new SessionExpiredPage(Page);
    var isVisible = await sessionExpiredPage.IsSessionExpiredModalVisibleAsync();
    isVisible.ShouldBeTrue();
}
```

**Why This Works:**
- ✅ In-process exception (no HTTP layer mocking needed)
- ✅ Works with all 3 stub clients (Inventory, Orders, CustomerIdentity)
- ✅ Simulates real 401 response behavior
- ✅ Triggers SessionExpiredService event globally

**What to Stub:**
- All API clients that require authentication
- All methods that might be called during user workflows
- Example clients: StubInventoryClient, StubOrdersClient, StubCustomerIdentityClient

---

### Pattern: Multi-Role Admin User Seeding

**Problem:** Authorization tests need deterministic admin users with specific roles (system-admin, operations-manager, warehouse-clerk, customer-service, copy-writer, pricing-manager, executive).

**Solution:** Extend E2ETestFixture with `SeedAdminUserWithRole()` method + WellKnownTestData constants.

**Implementation:**

```csharp
// tests/Backoffice/Backoffice.E2ETests/WellKnownTestData.cs
internal static class WellKnownTestData
{
    internal static class AdminUsers
    {
        // Multi-role admin users for Authorization scenarios
        public static readonly Guid SystemAdmin = Guid.Parse("AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA");
        public const string SystemAdminEmail = "sysadmin@crittersupply.com";
        public const string SystemAdminRole = "system-admin";

        public static readonly Guid OperationsManager = Guid.Parse("BBBBBBBB-BBBB-BBBB-BBBB-BBBBBBBBBBBB");
        public const string OperationsManagerEmail = "opsmgr@crittersupply.com";
        public const string OperationsManagerRole = "operations-manager";

        public static readonly Guid WarehouseClerk = Guid.Parse("CCCCCCCC-CCCC-CCCC-CCCC-CCCCCCCCCCCC");
        public const string WarehouseClerkEmail = "warehouse@crittersupply.com";
        public const string WarehouseClerkRole = "warehouse-clerk";

        public static readonly Guid CustomerService = Guid.Parse("DDDDDDDD-DDDD-DDDD-DDDD-DDDDDDDDDDDD");
        public const string CustomerServiceEmail = "support@crittersupply.com";
        public const string CustomerServiceRole = "customer-service";

        public static readonly Guid CopyWriter = Guid.Parse("EEEEEEEE-EEEE-EEEE-EEEE-EEEEEEEEEEEE");
        public const string CopyWriterEmail = "copywriter@crittersupply.com";
        public const string CopyWriterRole = "copy-writer";

        public static readonly Guid PricingManager = Guid.Parse("FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF");
        public const string PricingManagerEmail = "pricing@crittersupply.com";
        public const string PricingManagerRole = "pricing-manager";

        public static readonly Guid Executive = Guid.Parse("99999999-9999-9999-9999-999999999999");
        public const string ExecutiveEmail = "exec@crittersupply.com";
        public const string ExecutiveRole = "executive";
    }
}
```

```csharp
// tests/Backoffice/Backoffice.E2ETests/E2ETestFixture.cs
public void SeedAdminUserWithRole(Guid userId, string email, string displayName, string role)
{
    var passwordHasher = new PasswordHasher<BackofficeUser>();

    // Map role string to BackofficeRole enum
    var backofficeRole = role switch
    {
        "system-admin" => BackofficeIdentity.Identity.BackofficeRole.SystemAdmin,
        "operations-manager" => BackofficeIdentity.Identity.BackofficeRole.OperationsManager,
        "warehouse-clerk" => BackofficeIdentity.Identity.BackofficeRole.WarehouseClerk,
        "customer-service" => BackofficeIdentity.Identity.BackofficeRole.CustomerService,
        "copy-writer" => BackofficeIdentity.Identity.BackofficeRole.CopyWriter,
        "pricing-manager" => BackofficeIdentity.Identity.BackofficeRole.PricingManager,
        "executive" => BackofficeIdentity.Identity.BackofficeRole.Executive,
        _ => throw new ArgumentException($"Unknown role: {role}")
    };

    var user = new BackofficeUser(
        userId,
        email,
        displayName,
        backofficeRole,
        IsActive: true
    );

    // Hash password using ASP.NET Core Identity's PBKDF2-SHA256
    user.PasswordHash = passwordHasher.HashPassword(user, "Password123!");

    _backofficeIdentityDbContext.BackofficeUsers.Add(user);
    _backofficeIdentityDbContext.SaveChanges();
}
```

**Step Definition Usage:**

```csharp
// tests/Backoffice/Backoffice.E2ETests/StepDefinitions/AuthorizationSteps.cs
[Given(@"I am logged in as a ""(.*)"" user")]
public async Task GivenIAmLoggedInAsAUser(string role)
{
    // Map role to well-known test data
    var (userId, email, displayName) = role switch
    {
        "system-admin" => (WellKnownTestData.AdminUsers.SystemAdmin,
                           WellKnownTestData.AdminUsers.SystemAdminEmail,
                           "System Admin"),
        "warehouse-clerk" => (WellKnownTestData.AdminUsers.WarehouseClerk,
                              WellKnownTestData.AdminUsers.WarehouseClerkEmail,
                              "Warehouse Clerk"),
        _ => throw new ArgumentException($"Unknown role: {role}")
    };

    // Seed user with specific role
    Fixture.SeedAdminUserWithRole(userId, email, displayName, role);

    // Log in
    var loginPage = new LoginPage(Page, Fixture.WasmBaseUrl);
    await loginPage.NavigateAsync();
    await loginPage.LoginAsync(email, "Password123!");
}
```

**Why This Works:**
- ✅ Deterministic GUIDs (repeatable tests)
- ✅ Role-specific permissions enforced by Backoffice Identity BC
- ✅ Uses production PasswordHasher (PBKDF2-SHA256)
- ✅ Supports all 7 BackofficeRole enum values

**When to Use:**
- Authorization/RBAC tests
- Role-specific feature visibility tests
- Permission-based workflow tests

---

### Pattern: Role Enum Mismatch Resolution

**Problem:** Test data used "finance-clerk" role, but production enum uses `PricingManager` (not `FinanceClerk`).

**Error:**
```
CS0117: 'BackofficeRole' does not contain a definition for 'FinanceClerk'
```

**Root Cause:** Switch statement in E2ETestFixture.cs mapped "finance-clerk" to non-existent enum value.

**Solution:** Always check production enum FIRST before writing test data.

**Investigation Process:**

```bash
# Step 1: Find production enum definition
grep -A 20 "enum BackofficeRole" src/Backoffice\ Identity/BackofficeIdentity/Identity/BackofficeUser.cs

# Output shows actual enum values:
public enum BackofficeRole
{
    CopyWriter = 1,
    PricingManager = 2,        // Not FinanceClerk!
    WarehouseClerk = 3,
    CustomerService = 4,
    OperationsManager = 5,
    Executive = 6,
    SystemAdmin = 7
}
```

**Fix Applied:**

```csharp
// E2ETestFixture.cs - BEFORE (wrong)
var backofficeRole = role switch
{
    // ... other roles
    "finance-clerk" => BackofficeIdentity.Identity.BackofficeRole.FinanceClerk,  // ❌ Doesn't exist
    // ... other roles
};

// E2ETestFixture.cs - AFTER (correct)
var backofficeRole = role switch
{
    // ... other roles
    "pricing-manager" => BackofficeIdentity.Identity.BackofficeRole.PricingManager,  // ✅ Correct
    // ... other roles
};
```

**WellKnownTestData Update:**

```csharp
// BEFORE (wrong)
public static readonly Guid FinanceClerk = Guid.Parse("FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF");
public const string FinanceClerkEmail = "finance@crittersupply.com";
public const string FinanceClerkRole = "finance-clerk";

// AFTER (correct)
public static readonly Guid PricingManager = Guid.Parse("FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF");
public const string PricingManagerEmail = "pricing@crittersupply.com";
public const string PricingManagerRole = "pricing-manager";
```

**Key Lesson:** **NEVER** assume enum values — always verify production code first.

**Checklist for Role/Enum Testing:**
1. ✅ Grep production enum definition
2. ✅ Map kebab-case strings to actual enum values
3. ✅ Update both E2ETestFixture switch statement AND WellKnownTestData constants
4. ✅ Build succeeds with 0 errors

---

### Pattern: Playwright `.First` Property (Not Method)

**Problem:** Used LINQ-style `.First()` method but Playwright uses `.First` property.

**Error:**
```
CS1061: 'ILocator' does not contain a definition for 'First' and no accessible extension method 'First' accepting a first argument of type 'ILocator' could be found
```

**Root Cause:** Playwright's `ILocator` interface uses properties, not methods, for indexing.

**Wrong:**
```csharp
public async Task ClickFirstAlertAsync()
{
    var firstAlert = AlertRows.First();  // ❌ Method call
    await firstAlert.ClickAsync();
}

public async Task ClickAlertBySku(string sku)
{
    var alertRow = AlertRows.Filter(new() { HasText = sku }).First();  // ❌ Method call
    await alertRow.ClickAsync();
}
```

**Right:**
```csharp
public async Task ClickFirstAlertAsync()
{
    var firstAlert = AlertRows.First;  // ✅ Property access
    await firstAlert.ClickAsync();
    await AlertDetailsModal.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
}

public async Task ClickAlertBySku(string sku)
{
    // Find alert row by SKU text content
    var alertRow = AlertRows.Filter(new() { HasText = sku }).First;  // ✅ Property access
    await alertRow.ClickAsync();
    await AlertDetailsModal.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
}
```

**Playwright Indexing Properties:**

| Property | Purpose | Example |
|----------|---------|---------|
| `.First` | First matching element | `AlertRows.First` |
| `.Last` | Last matching element | `AlertRows.Last` |
| `.Nth(n)` | Nth matching element (0-indexed) | `AlertRows.Nth(2)` |

**Key Lesson:** Playwright uses properties for indexing, not LINQ methods.

**When to Use:**
- Clicking first/last item in a list
- Iterating through filtered elements
- Selecting specific items by index

---

### Pattern: Session Expiry Modal as Global Page Object

**Problem:** Session expiry modal appears across all pages — don't duplicate locators in every page object.

**Solution:** Create dedicated `SessionExpiredPage.cs` page object with modal-specific locators and actions.

**Implementation:**

```csharp
// tests/Backoffice/Backoffice.E2ETests/Pages/SessionExpiredPage.cs
public sealed class SessionExpiredPage
{
    private readonly IPage _page;

    public SessionExpiredPage(IPage page)
    {
        _page = page;
    }

    // Locators - Session Expired Modal (global overlay)
    private ILocator SessionExpiredModal => _page.GetByTestId("session-expired-modal");
    private ILocator ModalMessage => SessionExpiredModal.Locator("[data-testid='modal-message']");
    private ILocator LogInAgainButton => SessionExpiredModal.GetByTestId("log-in-again-button");
    private ILocator CloseButton => SessionExpiredModal.GetByTestId("close-button");

    // Actions
    public async Task<bool> IsSessionExpiredModalVisibleAsync()
    {
        try
        {
            await SessionExpiredModal.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    public async Task<string?> GetModalMessageAsync()
    {
        if (await IsSessionExpiredModalVisibleAsync())
        {
            return await ModalMessage.TextContentAsync();
        }
        return null;
    }

    public async Task ClickLogInAgainAsync()
    {
        await LogInAgainButton.ClickAsync();
        await _page.WaitForURLAsync("**/auth/login**");
    }

    public async Task<bool> CanInteractWithPageBehindModalAsync()
    {
        // Modal should block interaction with page behind it
        try
        {
            await _page.GetByTestId("dashboard-title").ClickAsync(new() { Timeout = 1_000 });
            return true;  // Should not reach here if modal blocks
        }
        catch (TimeoutException)
        {
            return false;  // Modal correctly blocks interaction
        }
    }

    public async Task<int> GetSessionExpiredModalCountAsync()
    {
        // Verify no duplicate modals
        return await _page.GetByTestId("session-expired-modal").CountAsync();
    }

    public async Task CloseModalAsync()
    {
        await CloseButton.ClickAsync();
        await SessionExpiredModal.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 3_000 });
    }

    public async Task<bool> IsModalHiddenAsync()
    {
        try
        {
            await SessionExpiredModal.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 2_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }
}
```

**Why Separate Page Object:**
- ✅ Modal appears globally (not tied to specific page)
- ✅ Avoids duplicating locators across DashboardPage, CustomerSearchPage, OperationsAlertsPage, etc.
- ✅ Testable modal behavior (blocking interaction, returnUrl preservation, duplicate detection)
- ✅ Reusable across all SessionExpirySteps scenarios

**When to Create Global Page Objects:**
- Modals/overlays that appear across multiple pages
- Navigation bars (shared across all authenticated pages)
- Footer components
- Toast notifications

---

### Pattern: ReturnUrl Preservation Testing

**Problem:** After session expiry, user should be redirected back to original page after re-authentication.

**Solution:** Verify `returnUrl` query parameter is preserved and page is restored post-login.

**Implementation:**

```csharp
// tests/Backoffice/Backoffice.E2ETests/StepDefinitions/SessionExpirySteps.cs
[Then(@"the returnUrl query parameter should be ""(.*)""")]
public async Task ThenTheReturnUrlQueryParameterShouldBe(string expectedReturnUrl)
{
    await Task.Delay(500); // Allow navigation to complete
    var url = Page.Url;
    url.ShouldContain($"returnUrl={Uri.EscapeDataString(expectedReturnUrl)}");
}

[Then(@"I should be redirected back to the dashboard")]
public async Task ThenIShouldBeRedirectedBackToTheDashboard()
{
    var dashboardPage = new DashboardPage(Page, Fixture.WasmBaseUrl);
    var isOnDashboard = await dashboardPage.IsOnDashboardPageAsync();
    isOnDashboard.ShouldBeTrue();
}

[Then(@"I should be redirected back to the operations alerts page")]
public async Task ThenIShouldBeRedirectedBackToTheOperationsAlertsPage()
{
    var alertsPage = new OperationsAlertsPage(Page, Fixture.WasmBaseUrl);
    var isOnAlerts = await alertsPage.IsOnOperationsAlertsPageAsync();
    isOnAlerts.ShouldBeTrue();
}
```

**Gherkin Scenario:**

```gherkin
Scenario: Session expiry on dashboard preserves returnUrl
  Given I am logged in as "alice.admin@crittersupply.com" with password "Password123!"
  And I am on the dashboard
  When my session expires
  And I trigger a data refresh
  Then I should see the session expired modal
  When I click "Log In Again"
  Then I should be redirected to the login page
  And the returnUrl query parameter should be "/dashboard"
  When I log in as "alice.admin@crittersupply.com" with password "Password123!"
  Then I should be redirected back to the dashboard

Scenario: Session expiry on operations alerts page preserves returnUrl
  Given I am logged in as "alice.admin@crittersupply.com" with password "Password123!"
  And I am on the operations alerts page
  When my session expires
  And I try to acknowledge an alert
  Then I should see the session expired modal
  When I click "Log In Again"
  Then I should be redirected to the login page
  And the returnUrl query parameter should be "/operations/alerts"
  When I log in as "alice.admin@crittersupply.com" with password "Password123!"
  Then I should be redirected back to the operations alerts page
  And I should see the operations alerts feed
```

**Why This Matters:**
- ✅ User returns to exact page they were working on
- ✅ No lost context (filters, scroll position preserved by page state)
- ✅ Matches real-world UX expectations
- ✅ Validates navigation guard + returnUrl wiring

**Key Assertions:**
1. `returnUrl` query parameter present and correctly URL-encoded
2. Post-login redirect lands on original page
3. Page-specific elements visible (proves full navigation completed)

---

### M32.2 Key Learnings

**1. Stub Client Session Expiry Pattern**
- ✅ **DO** add `SimulateSessionExpired` bool property to all API client stubs
- ✅ **DO** throw `HttpRequestException` with `HttpStatusCode.Unauthorized`
- ✅ **DO** check this flag at the start of EVERY API method
- ❌ **DON'T** mock HttpClient directly (in-process exceptions are simpler)

**2. Multi-Role Admin User Seeding**
- ✅ **DO** use deterministic GUIDs in WellKnownTestData
- ✅ **DO** check production enum values BEFORE writing test data
- ✅ **DO** map kebab-case strings to actual enum values
- ❌ **DON'T** assume role names match enum values (verify first!)

**3. Playwright Indexing**
- ✅ **DO** use `.First` property (not `.First()` method)
- ✅ **DO** use `.Last` property, `.Nth(n)` method
- ❌ **DON'T** use LINQ extension methods on `ILocator`

**4. Global Page Objects**
- ✅ **DO** create separate page objects for global modals/overlays
- ✅ **DO** verify modal blocks interaction with page behind
- ✅ **DO** check for duplicate modals (should always be exactly 1)
- ❌ **DON'T** duplicate modal locators in every page object

**5. ReturnUrl Preservation**
- ✅ **DO** verify `returnUrl` query parameter is URL-encoded
- ✅ **DO** assert user lands back on original page post-login
- ✅ **DO** check page-specific elements are visible (proves full navigation)
- ❌ **DON'T** just check URL — verify page state is restored

**6. Authorization Testing**
- ✅ **DO** seed role-specific admin users for RBAC tests
- ✅ **DO** verify 403 responses for insufficient permissions
- ✅ **DO** check UI elements hidden/disabled based on roles
- ❌ **DON'T** skip server-side enforcement tests (UI hiding is not enough)

**7. Build Verification**
- ✅ **DO** run `dotnet build` after adding step definitions
- ✅ **DO** fix all compile errors before attempting test execution
- ✅ **DO** verify 0 errors (warnings OK if pre-existing)
- ❌ **DON'T** rely on IDE intellisense alone — full build catches more issues

**8. CI/Local Test Execution**
- ✅ **DO** run E2E tests in CI (TestContainers + Playwright environment)
- ✅ **DO** verify build succeeds locally (validates all code is correct)
- ❌ **DON'T** attempt full E2E test execution without TestContainers + Playwright setup

---

### M32.2 Reference Files

**Feature Files:**
- `tests/Backoffice/Backoffice.E2ETests/Features/SessionExpiry.feature` (P0-3)
- `tests/Backoffice/Backoffice.E2ETests/Features/Authorization.feature` (P0-1)
- `tests/Backoffice/Backoffice.E2ETests/Features/DataFreshness.feature` (P1-2)
- `tests/Backoffice/Backoffice.E2ETests/Features/AlertAcknowledgment.feature` (P0-2)

**Step Definitions:**
- `SessionExpirySteps.cs` - 17 step definitions
- `AuthorizationSteps.cs` - 12 step definitions
- `OperationsAlertsSteps.cs` - Extended with P0-2/P1-2 steps

**Page Objects:**
- `SessionExpiredPage.cs` - Global modal page object
- `OperationsAlertsPage.cs` - Extended with data freshness methods
- `CustomerSearchPage.cs` - Extended with session expiry support

**Test Infrastructure:**
- `E2ETestFixture.cs` - `SeedAdminUserWithRole()` method
- `WellKnownTestData.cs` - Multi-role admin user constants
- `StubInventoryClient.cs`, `StubOrdersClient.cs`, `StubCustomerIdentityClient.cs` - SimulateSessionExpired

**Production Enum:**
- `src/Backoffice Identity/BackofficeIdentity/Identity/BackofficeUser.cs` - BackofficeRole enum (7 values)

---
