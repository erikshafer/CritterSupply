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

## Pattern 3: MudBlazor `MudSelect` Interaction

MudBlazor's `MudSelect` renders options in an animated popover that opens asynchronously. The popup has a CSS transition — during the animation, the `[role='option']` elements may be in the DOM but not yet "visible" in Playwright's strict sense (non-zero dimensions, not hidden by parent opacity/visibility).

### Correct Pattern — Locator `ClickAsync` with Built-In Auto-Wait

```csharp
public async Task SelectAddressByNicknameAsync(string nickname)
{
    // Wait for the dropdown to become interactable
    await AddressSelect.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
    await AddressSelect.ClickAsync();

    // Use Playwright's Locator.ClickAsync() with built-in actionability checks.
    // This retries until the element is visible, stable, enabled, and not covered —
    // handling MudBlazor's CSS animation delay without a fragile WaitForSelectorAsync call.
    //
    // :has-text() matches the full display string (e.g., "Home - 123 Main St, Seattle, WA 98101"),
    // not just the nickname, so partial matches work correctly.
    await page.Locator($"[role='option']:has-text('{nickname}')")
              .ClickAsync(new LocatorClickOptions { Timeout = 15_000 });
}
```

### Why Not `WaitForSelectorAsync` + `ClickAsync`?

The two-step pattern is fragile under CI load and MudBlazor's CSS animations:

```csharp
// FRAGILE — can time out when run as the Nth scenario in a long test run.
// WaitForSelectorAsync checks visibility at the moment of evaluation; if the
// popup animation has briefly made [role='option'] non-visible, the check fails
// even though the popup is opening correctly.
await page.WaitForSelectorAsync("[role='option']", new PageWaitForSelectorOptions { Timeout = 10_000 });
await page.Locator($"[role='option']:has-text('{nickname}')").ClickAsync();
```

**Root cause of CI failures:** In a longer test run (5th+ scenario), Blazor Server's SignalR circuit latency can delay the popover render enough to miss the 10 s `WaitForSelectorAsync` window. Playwright's `Locator.ClickAsync()` retries internally until the element is actionable, which is more tolerant of transient animation/render delays.

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

**Fix:** Use `Locator($"[role='option']:has-text('{nickname}')").ClickAsync(new LocatorClickOptions { Timeout = 15_000 })`. Playwright's `Locator.ClickAsync` retries through CSS animation delays automatically.

---

### 4. `WaitForSelectorAsync("[role='option']")` Times Out in Later Scenarios

**Symptom:** `TimeoutException: waiting for Locator("[role='option']") to be visible` — but only in the 5th or 6th scenario; earlier scenarios pass fine.

**Cause:** Blazor Server's SignalR circuit (the framework circuit, not the CritterSupply hub) processes events from the browser through a round-trip to the .NET server. Under CI load or after several Kestrel circuits have been created, this round-trip can take longer. `WaitForSelectorAsync` evaluates visibility **at a single moment** — if the popup animation is mid-render at that moment, it fails. `Locator.ClickAsync` retries internally until the element is actionable.

**Fix:** Replace the two-step pattern with a single `Locator.ClickAsync`:
```csharp
// Before (fragile):
await page.WaitForSelectorAsync("[role='option']", new PageWaitForSelectorOptions { Timeout = 10_000 });
await page.Locator($"[role='option']:has-text('{nickname}')").ClickAsync();

// After (robust):
await page.Locator($"[role='option']:has-text('{nickname}')")
          .ClickAsync(new LocatorClickOptions { Timeout = 15_000 });
```

---

### 5. Stubs Return Wrong Data Because Reset Was Skipped

**Symptom:** Scenario B picks up cart data seeded by Scenario A.

**Cause:** Stubs hold in-memory state across scenarios if `Clear()` is not called between them.

**Fix:** Call `_fixture.ClearAllStubs()` in a `[BeforeScenario]` hook with `Order = 2` (runs before data seeding at `Order = 3`).

---

### 6. SignalR Negotiation Fails With 400

**Symptom:** SignalR connection on the order confirmation page fails during the WebSocket upgrade.

**Cause:** ASP.NET Core 10+ antiforgery middleware blocks the SignalR negotiate endpoint by default.

**Fix:** Add `.DisableAntiforgery()` to the hub mapping in `Storefront.Api/Program.cs`.

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
- [ ] Tag with `@wip` if not yet implemented (so it runs but is skipped)
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
