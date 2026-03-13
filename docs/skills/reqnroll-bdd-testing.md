# Reqnroll BDD Testing

Patterns for writing behavior-driven development (BDD) tests using Reqnroll and Gherkin in CritterSupply, with primary focus on end-to-end (E2E) browser testing and integration test scenarios.

---

## Table of Contents

1. [When to Use This Skill](#when-to-use-this-skill)
2. [Overview](#overview)
3. [Core Principles](#core-principles)
4. [Project Setup](#project-setup)
5. [Gherkin Feature Files](#gherkin-feature-files)
6. [Step Definitions](#step-definitions)
7. [Hooks Lifecycle](#hooks-lifecycle)
8. [Integration with Test Infrastructure](#integration-with-test-infrastructure)
9. [Tagging Strategy](#tagging-strategy)
10. [Running Tests](#running-tests)
11. [Best Practices](#best-practices)
12. [Lessons Learned](#lessons-learned)
13. [Troubleshooting](#troubleshooting)
14. [Appendix](#appendix)

---

## When to Use This Skill

**Use Reqnroll + BDD when:**
- Writing user-facing E2E browser tests with Playwright (checkout flow, cart management, real-time updates)
- Creating living documentation for complex cross-BC user journeys
- Collaborating with non-technical stakeholders on requirements validation
- Testing complete vertical slices that require business-readable specifications
- Verifying SignalR real-time updates that require browser interaction

**Do NOT use Reqnroll for:**
- Simple CRUD API tests — use Alba integration tests directly
- Pure business logic — use unit tests with xUnit + Shouldly
- Internal BC-to-BC message flows without UI — use Alba + `ExecuteAndWaitAsync`
- Single-endpoint HTTP contract verification — Alba is more direct

> **Key Insight:** In CritterSupply, Reqnroll is primarily used with Playwright for E2E browser testing (Storefront.E2ETests, VendorPortal.E2ETests). Alba-only integration tests rarely need Gherkin unless stakeholder communication is critical.

---

## Overview

### What is Reqnroll?

Reqnroll is an open-source .NET BDD framework that executes Gherkin specifications as automated tests. It's a community-driven fork of SpecFlow, created after the Tricentis acquisition to maintain a fully open-source BDD option for .NET.

**Why CritterSupply uses Reqnroll:**
- **Open source** with no paid tiers or licensing restrictions (BSD-3-Clause)
- **SpecFlow-compatible** — 100% Gherkin-compatible, familiar patterns for developers with SpecFlow experience
- **xUnit integration** — First-class support via `Reqnroll.xUnit` package
- **Living documentation** — `.feature` files serve as executable specifications
- **Client alignment** — External stakeholders requested Reqnroll usage

See [ADR 0006: Reqnroll for BDD Testing](../decisions/0006-reqnroll-bdd-framework.md) for the full decision rationale.

### Where Reqnroll Fits in CritterSupply's Testing Strategy

```
┌─────────────────────────────────────────────────────────────┐
│ Unit Tests (xUnit + Shouldly)                               │
│ • Pure functions (decider pattern, domain logic)            │
│ • Business rule validation                                  │
│ • No external dependencies                                  │
└─────────────────────────────────────────────────────────────┘
                          ▼
┌─────────────────────────────────────────────────────────────┐
│ Integration Tests (Alba + TestContainers)                   │
│ • Single-BC HTTP endpoints                                  │
│ • Wolverine handler workflows                               │
│ • Marten aggregate persistence                              │
│ • Message publishing verification                           │
└─────────────────────────────────────────────────────────────┘
                          ▼
┌─────────────────────────────────────────────────────────────┐
│ E2E Tests (Playwright + Reqnroll + Alba)                    │
│ • User journeys through Blazor UI                           │
│ • SignalR real-time updates                                 │
│ • Cross-BC integration via browser                          │
│ • MudBlazor component interactions                          │
│ • Living documentation for stakeholders                     │
└─────────────────────────────────────────────────────────────┘
```

**Reqnroll is used in the bottom layer** — E2E tests where business-readable specifications add the most value.

---

## Core Principles

1. **Reserve BDD for High-Value Scenarios** — Use Reqnroll for complex user flows; keep simple CRUD tests as Alba-only
2. **Gherkin for Behavior, Not Implementation** — Focus on user actions and outcomes, not internal mechanics
3. **Living Documentation** — `.feature` files must stay synchronized with tests (tests fail if behavior diverges)
4. **Integration with Existing Test Infrastructure** — Reqnroll scenarios reuse Alba + TestContainers + Playwright fixtures
5. **Tagging for Test Organization** — Use tags (@checkout, @signalr, @ignore) to filter and organize scenarios
6. **Deterministic Test Data** — No random IDs; use `WellKnownTestData` constants for E2E reproducibility

---

## Project Setup

### Required NuGet Packages

```xml
<ItemGroup>
  <PackageReference Include="Reqnroll" />
  <PackageReference Include="Reqnroll.xUnit" />
  <PackageReference Include="xunit" />
  <PackageReference Include="xunit.runner.visualstudio" />
  <PackageReference Include="Shouldly" />

  <!-- For E2E browser tests -->
  <PackageReference Include="Microsoft.Playwright" />
  <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" />

  <!-- For integration tests -->
  <PackageReference Include="Alba" />
  <PackageReference Include="Testcontainers.PostgreSql" />
</ItemGroup>
```

### Global Usings (Optional)

```xml
<ItemGroup>
  <Using Include="Xunit" />
  <Using Include="Shouldly" />
  <Using Include="Reqnroll" />
  <Using Include="Microsoft.Playwright" /> <!-- If using E2E -->
</ItemGroup>
```

### xUnit Configuration (Disable Parallelization)

E2E tests with shared browser contexts should run sequentially. Create `xunit.runner.json`:

```json
{
  "$schema": "https://xunit.net/schema/current/xunit.runner.schema.json",
  "parallelizeAssembly": false,
  "parallelizeTestCollections": false
}
```

Add to `.csproj`:

```xml
<ItemGroup>
  <None Update="xunit.runner.json">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </None>
</ItemGroup>
```

### Reqnroll Configuration (Optional)

Create `reqnroll.json` in the test project root for advanced configuration:

```json
{
  "$schema": "https://schemas.reqnroll.net/reqnroll-config-latest.json",
  "language": {
    "feature": "en"
  },
  "bindingCulture": {
    "name": "en-US"
  },
  "trace": {
    "traceSuccessfulSteps": false,
    "traceTimings": true,
    "minTracedDuration": "0:0:0.1"
  }
}
```

---

## Gherkin Feature Files

### File Organization

Feature files live in **two locations**:

1. **Specification source:** `docs/features/<BC-name>/` — Single source of truth, versioned in git
2. **Test project:** `tests/<BC-name>/<Project>.E2ETests/Features/` — Copied for Reqnroll code generation

```
docs/features/
├── customer-experience/
│   ├── cart-real-time-updates.feature
│   ├── checkout-flow.feature
│   ├── product-browsing.feature
│   └── storefront-protected-routes.feature
├── orders/
│   └── order-saga-orchestration.feature
├── returns/
│   ├── exchange-workflow.feature
│   ├── return-eligibility.feature
│   └── return-inspection.feature
└── vendor-portal/
    ├── vendor-auth.feature
    ├── vendor-change-requests.feature
    └── vendor-dashboard.feature
```

**Copying workflow:**

```bash
# Copy feature file from docs/ to test project
cp docs/features/customer-experience/checkout-flow.feature \
   "tests/Customer Experience/Storefront.E2ETests/Features/checkout-flow.feature"
```

> **⚠️ Important:** Reqnroll generates test code from `.feature` files at **build time**. Feature files must exist in the test project directory tree. If you add/modify a feature file, **rebuild the project** to regenerate bindings.

### Gherkin Syntax

```gherkin
Feature: Checkout Flow
  As a customer who has items in my cart
  I want to complete the checkout process step-by-step
  So that I can place an order with my shipping and payment information

  Background:
    Given I am logged in as "alice@example.com"
    And I have an active cart with 2 items

  Scenario: Complete checkout successfully
    Given I navigate to the cart page
    When I click "Proceed to Checkout"
    Then I should be on the checkout page
    And the checkout wizard should be visible

    When I select the saved address "Home"
    And I click "Continue"
    Then I should see the shipping method selection

    When I select "Standard Ground" shipping
    And I click "Continue"
    Then I should see the payment form

    When I enter payment token "tok_visa_test"
    And I click "Place Order"
    Then I should see the order confirmation page
    And the order status should be "Placed"

  Scenario: Cannot proceed to checkout with empty cart
    Given I have an empty cart
    And I navigate to the cart page
    Then the "Proceed to Checkout" button should be disabled
```

**Key elements:**
- **Feature** — High-level capability description (user perspective)
- **Background** — Setup steps run before **each** scenario
- **Scenario** — Specific test case with Given/When/Then steps
- **Tags** — Metadata for filtering (see [Tagging Strategy](#tagging-strategy))

### Using Tables for Complex Data

```gherkin
Scenario: Add product with images
  Given I have a product with SKU "CAT-TOY-05"
  And the product has the following images:
    | Url                                    | AltText           | DisplayOrder |
    | https://example.com/cat-laser-01.jpg   | Cat laser pointer | 0            |
    | https://example.com/cat-laser-02.jpg   | Laser in use      | 1            |
  When I add the product to the catalog
  Then the product should have 2 images
```

**Step definition for tables:**

```csharp
[Given(@"the product has the following images:")]
public void GivenTheProductHasTheFollowingImages(Table table)
{
    var images = table.Rows.Select(row => new ProductImageDto(
        row["Url"],
        row["AltText"],
        int.Parse(row["DisplayOrder"])
    )).ToList();

    _command = _command with { Images = images };
}
```

> **Reference:** [Gherkin Syntax Documentation](https://cucumber.io/docs/gherkin/reference/)

---

## Step Definitions

Step definitions are C# methods that implement Gherkin steps using `[Binding]` and regex attributes.

### Basic Step Definition Pattern

```csharp
using Marten;
using ProductCatalog.Api.Products;
using ProductCatalog.Products;

namespace ProductCatalog.Api.IntegrationTests;

[Binding]
public sealed class AddProductSteps
{
    private readonly TestFixture _fixture;
    private readonly ScenarioContext _scenarioContext;

    private AddProduct? _command;
    private IScenarioResult? _result;

    public AddProductSteps(ScenarioContext scenarioContext)
    {
        // Reqnroll injects ScenarioContext automatically
        _scenarioContext = scenarioContext;

        // Retrieve shared fixture from ScenarioContext (set by Hooks)
        _fixture = scenarioContext.Get<TestFixture>("Fixture");
    }

    [Given(@"I have a product with SKU ""(.*)""")]
    public void GivenIHaveAProductWithSku(string sku)
    {
        _command = new AddProduct(sku, "", "", "");
    }

    [Given(@"the product name is ""(.*)""")]
    public void GivenTheProductNameIs(string name)
    {
        _command = _command with { Name = name };
    }

    [When(@"I add the product to the catalog")]
    public async Task WhenIAddTheProductToTheCatalog()
    {
        _result = await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(_command).ToUrl("/api/products");
        });

        _scenarioContext["sku"] = _command!.Sku;
    }

    [Then(@"the product should be successfully created")]
    public void ThenTheProductShouldBeSuccessfullyCreated()
    {
        _result.ShouldNotBeNull();
        _result.Context.Response.StatusCode.ShouldBe(201);
    }

    [Then(@"the product should be retrievable by SKU ""(.*)""")]
    public async Task ThenTheProductShouldBeRetrievableBySku(string sku)
    {
        using var session = _fixture.GetDocumentSession();
        var product = await session.LoadAsync<Product>(sku);

        product.ShouldNotBeNull();
        product.Sku.Value.ShouldBe(sku);
    }
}
```

### Step Attributes

| Attribute | Purpose | Example |
|-----------|---------|---------|
| `[Given]` | Preconditions, setup | `Given the product catalog is empty` |
| `[When]` | Actions, triggers | `When I add the product to the catalog` |
| `[Then]` | Assertions, outcomes | `Then the product should be successfully created` |

**Regex patterns for parameter extraction:**
- `"(.*)"` — Captures quoted strings (e.g., `"alice@example.com"`)
- `(.*)` — Captures unquoted values (numbers, booleans)
- `"([^"]*)"` — Captures strings (non-greedy, prevents overlapping matches)

### Sharing State with ScenarioContext

ScenarioContext is a key-value store shared across all step definitions in a scenario:

```csharp
// Store data in one step
[When(@"I add the product to the catalog")]
public async Task WhenIAddTheProductToTheCatalog()
{
    // ...
    _scenarioContext["sku"] = _command!.Sku;
    _scenarioContext.Set(_command.Sku, "ProductSku"); // Typed alternative
}

// Retrieve data in another step
[Then(@"the product status should be ""(.*)""")]
public async Task ThenTheProductStatusShouldBe(string expectedStatus)
{
    var sku = _scenarioContext.Get<string>("ProductSku");

    using var session = _fixture.GetDocumentSession();
    var product = await session.LoadAsync<Product>(sku);

    product.Status.ToString().ShouldBe(expectedStatus);
}
```

**ScenarioContext best practices:**
- Use typed keys (constants or typed `Get<T>()` calls) to avoid typos
- Store minimal state — prefer immutable command/query objects
- Clear state in `[AfterScenario]` hooks if needed (though not typically required)

### E2E-Specific Step Patterns (Playwright)

E2E tests delegate browser interaction to **Page Object Models**:

```csharp
[Binding]
public sealed class CheckoutFlowStepDefinitions
{
    private readonly ScenarioContext _scenarioContext;
    private IPage Page => _scenarioContext.Get<IPage>("Page");

    private LoginPage LoginPage => new(Page);
    private CartPage CartPage => new(Page);
    private CheckoutPage CheckoutPage => new(Page);

    public CheckoutFlowStepDefinitions(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
    }

    [Given(@"I am logged in as ""(.*)""")]
    public async Task GivenIAmLoggedInAs(string email)
    {
        await LoginPage.NavigateAsync();
        await LoginPage.LoginAsync(email, WellKnownTestData.Customers.AlicePassword);

        var isLoggedIn = await LoginPage.IsLoggedInAsync();
        isLoggedIn.ShouldBeTrue($"Login as '{email}' should succeed");
    }

    [When(@"I click ""Proceed to Checkout""")]
    public async Task WhenIClickProceedToCheckout()
    {
        await CartPage.ClickProceedToCheckoutAsync();
    }

    [Then(@"I should be on the checkout page")]
    public async Task ThenIShouldBeOnCheckoutPage()
    {
        await CheckoutPage.WaitForCheckoutLoadedAsync();
        var url = Page.Url;
        url.ShouldContain("/checkout");
    }
}
```

> **See:** [e2e-playwright-testing.md](./e2e-playwright-testing.md) for complete Page Object Model patterns, browser lifecycle management, and Playwright-specific guidance.

---

## Hooks Lifecycle

Hooks are special methods that run at specific points in the test lifecycle. CritterSupply uses hooks for fixture initialization, browser management, and test data seeding.

### Hook Execution Order

```
[BeforeTestRun]           ← Once per test assembly
    ↓
[BeforeFeature]           ← Once per .feature file
    ↓
[BeforeScenario(Order=1)] ← Before each scenario (multiple hooks can coexist)
[BeforeScenario(Order=2)]
[BeforeScenario(Order=10)]
    ↓
  ... Scenario steps execute ...
    ↓
[AfterScenario(Order=10)] ← After each scenario (reverse order)
[AfterScenario(Order=2)]
[AfterScenario(Order=1)]
    ↓
[AfterFeature]            ← Once per .feature file
    ↓
[AfterTestRun]            ← Once per test assembly
```

### Standard Hooks Pattern (E2E Tests)

CritterSupply E2E tests use multiple hook classes with ordering:

#### 1. Fixture Initialization Hook

Initializes the test fixture **once per test run** (TestContainers, Alba hosts, Playwright browser):

```csharp
namespace Storefront.E2ETests.Hooks;

[Binding]
public sealed class FixtureHooks
{
    private static E2ETestFixture? _fixture;
    private static IPlaywright? _playwright;
    private static IBrowser? _browser;

    [BeforeTestRun]
    public static async Task BeforeTestRun()
    {
        // 1. Initialize test fixture (TestContainers, Alba hosts)
        _fixture = new E2ETestFixture();
        await _fixture.InitializeAsync();

        // 2. Initialize Playwright and launch browser (shared across all scenarios)
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
    }

    [AfterTestRun]
    public static async Task AfterTestRun()
    {
        // Dispose in reverse order
        if (_browser != null) await _browser.CloseAsync();
        if (_playwright != null) _playwright.Dispose();
        if (_fixture != null) await _fixture.DisposeAsync();
    }

    [BeforeScenario(Order = 0)]
    public void InjectFixtureIntoScenarioContext(ScenarioContext scenarioContext)
    {
        // Make fixture, playwright, and browser available to step definitions
        scenarioContext.Set(_fixture!, ScenarioContextKeys.Fixture);
        scenarioContext.Set(_playwright!, ScenarioContextKeys.Playwright);
        scenarioContext.Set(_browser!, ScenarioContextKeys.Browser);
    }
}
```

#### 2. Browser Lifecycle Hook (Per Scenario)

Creates a fresh browser context and page for each scenario:

```csharp
namespace Storefront.E2ETests.Hooks;

[Binding]
public sealed class PlaywrightHooks
{
    private readonly ScenarioContext _scenarioContext;
    private IBrowserContext? _browserContext;

    public PlaywrightHooks(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
    }

    [BeforeScenario(Order = 10)]
    public async Task CreateBrowserContextAndPage()
    {
        var playwright = _scenarioContext.Get<IPlaywright>(ScenarioContextKeys.Playwright);
        var browser = _scenarioContext.Get<IBrowser>(ScenarioContextKeys.Browser);
        var fixture = _scenarioContext.Get<E2ETestFixture>(ScenarioContextKeys.Fixture);

        // Create fresh browser context per scenario (session isolation)
        _browserContext = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = fixture.StorefrontWebBaseUrl
        });

        // Start Playwright tracing (screenshots + network logs for failures)
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

        var testFailed = _scenarioContext.TestError != null;
        if (testFailed)
        {
            // Save trace for failed scenarios (CI artifact)
            var scenarioTitle = _scenarioContext.ScenarioInfo.Title
                .Replace(" ", "_")
                .Replace("/", "_");
            var traceDir = Path.Combine(
                Directory.GetCurrentDirectory(),
                "playwright-traces",
                $"{scenarioTitle}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.zip");

            Directory.CreateDirectory(Path.GetDirectoryName(traceDir)!);

            await _browserContext.Tracing.StopAsync(new TracingStopOptions
            {
                Path = traceDir
            });
        }
        else
        {
            await _browserContext.Tracing.StopAsync();
        }

        await _browserContext.CloseAsync();
        _browserContext = null;
    }
}
```

#### 3. Test Data Seeding Hook (Tag-Based)

Seeds test data for specific scenarios using tags:

```csharp
namespace Storefront.E2ETests.Hooks;

[Binding]
public sealed class DataHooks
{
    private readonly ScenarioContext _scenarioContext;

    public DataHooks(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
    }

    [BeforeScenario("@checkout", Order = 5)]
    public async Task SeedCheckoutScenarioData()
    {
        var fixture = _scenarioContext.Get<E2ETestFixture>(ScenarioContextKeys.Fixture);

        // Seed test data in stub clients (deterministic IDs)
        fixture.StubShoppingClient.SeedCart(
            WellKnownTestData.Carts.AliceCartId,
            WellKnownTestData.Customers.AliceId,
            items: new[]
            {
                new CartItemDto("DOG-BOWL-01", 2, 19.99m),
                new CartItemDto("CAT-TOY-05", 1, 29.99m)
            });

        fixture.StubCatalogClient.SeedProducts(
            new ProductDto("DOG-BOWL-01", "Ceramic Dog Bowl", 19.99m),
            new ProductDto("CAT-TOY-05", "Interactive Cat Laser", 29.99m));

        // Seed saved addresses in database via Alba
        await SeedCustomerAddressesAsync(fixture);
    }
}
```

### Hook Best Practices

1. **Use `Order` parameter** — Control hook execution sequence (lower numbers run first)
2. **Tag-based hooks** — `[BeforeScenario("@tag")]` runs only for tagged scenarios
3. **Single responsibility** — One hook class per concern (fixture, browser, data seeding)
4. **Dispose in reverse order** — `[AfterTestRun]` should dispose resources in reverse of initialization
5. **Share via ScenarioContext** — Inject fixtures/browser/page into ScenarioContext for step definition access

---

## Integration with Test Infrastructure

Reqnroll scenarios reuse CritterSupply's existing test infrastructure:

### Alba Integration (HTTP API Testing)

```csharp
[When(@"I add the product to the catalog")]
public async Task WhenIAddTheProductToTheCatalog()
{
    _result = await _fixture.Host.Scenario(s =>
    {
        s.Post.Json(_command).ToUrl("/api/products");
        s.StatusCodeShouldBe(201);
    });
}
```

### TestContainers (Real Postgres/RabbitMQ)

TestFixture initializes containers once per test run:

```csharp
public class E2ETestFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("storefront_test_db")
        .WithName($"storefront-postgres-test-{Guid.NewGuid():N}")
        .WithCleanUp(true)
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        // Initialize Alba hosts with container connection string
        // ...
    }
}
```

### Playwright (Browser Automation)

Page interactions use Playwright's async API:

```csharp
[When(@"I click ""(.*)""")]
public async Task WhenIClick(string buttonText)
{
    var page = _scenarioContext.Get<IPage>(ScenarioContextKeys.Page);
    await page.GetByRole(AriaRole.Button, new() { Name = buttonText }).ClickAsync();
}
```

> **See:** [testcontainers-integration-tests.md](./testcontainers-integration-tests.md) for TestContainers patterns, [e2e-playwright-testing.md](./e2e-playwright-testing.md) for Playwright guidance.

---

## Tagging Strategy

Tags are metadata annotations in Gherkin that enable filtering and organization:

```gherkin
@checkout @p0
Scenario: Complete checkout successfully
  # ...

@signalr @e2e-only
Scenario: Order confirmation receives real-time payment status
  # ...

@mobile @wip @ignore
Scenario: Checkout adapts to mobile screen size
  # ...
```

### Common Tags in CritterSupply

| Tag | Purpose | Example Usage |
|-----|---------|---------------|
| `@checkout` | Checkout flow scenarios | Triggers data seeding hook |
| `@signalr` | Real-time SignalR updates | Requires browser + hub injection |
| `@ignore` | Skip scenario (known issues) | MudBlazor dropdown failures |
| `@wip` | Work in progress | Future enhancements |
| `@p0` / `@p1` / `@p2` | Priority levels | Test execution phasing |
| `@e2e-only` | Requires full browser stack | Not testable via Alba alone |
| `@mobile` | Mobile viewport testing | Future responsive design tests |

### Running Tests by Tag

```bash
# Run only @checkout scenarios
dotnet test --filter "Category=checkout"

# Exclude @ignore scenarios
dotnet test --filter "Category!=ignore"

# Run P0 priority scenarios
dotnet test --filter "Category=p0"
```

### Tag-Based Hooks

```csharp
[BeforeScenario("@checkout", Order = 5)]
public async Task SeedCheckoutData()
{
    // Only runs for scenarios tagged @checkout
}
```

---

## Running Tests

### Command-Line Execution

```bash
# Run all Reqnroll scenarios
dotnet test --filter "DisplayName~Scenario"

# Run specific feature
dotnet test --filter "DisplayName~Checkout Flow"

# Run by tag (requires Reqnroll.xUnit configuration)
dotnet test --filter "Category=checkout"

# List all discovered scenarios
dotnet test --list-tests
```

### IDE Execution

- **Visual Studio:** Test Explorer shows Reqnroll scenarios as xUnit tests
- **Rider:** Run configurations can target `.feature` files or individual scenarios
- **VS Code:** Use .NET Test Explorer extension

### CI/CD Integration

```yaml
# GitHub Actions example
- name: Run E2E Tests
  run: dotnet test tests/Customer\ Experience/Storefront.E2ETests/ --no-build --logger trx

- name: Upload Playwright Traces
  if: failure()
  uses: actions/upload-artifact@v4
  with:
    name: playwright-traces
    path: tests/**/playwright-traces/*.zip
```

> **⚠️ Note:** Reqnroll generates xUnit test methods from Gherkin scenarios at build time. If scenarios don't appear in Test Explorer, **rebuild the project**.

---

## Best Practices

### 1. Reserve BDD for High-Value Scenarios

**Use Reqnroll for:**
- Complex user flows (checkout, order placement, multi-step wizards)
- Cross-BC integration requiring business-readable specs
- Real-time SignalR update verification
- Living documentation for non-technical stakeholders

**Use Alba-only for:**
- Simple CRUD operations
- Single-endpoint HTTP contract tests
- Internal API testing without UI involvement

**Example comparison:**

```gherkin
# GOOD — High-value user journey (Reqnroll)
Scenario: Customer completes 3-step checkout wizard
  Given I am logged in with items in my cart
  When I complete the checkout wizard
  Then my order should be placed
  And I should receive real-time payment confirmation
```

```csharp
// GOOD — Simple API test (Alba-only, no Reqnroll)
[Fact]
public async Task POST_CreateProduct_Returns201()
{
    await Host.Scenario(s =>
    {
        s.Post.Json(new CreateProduct("SKU-123", "Product Name")).ToUrl("/api/products");
        s.StatusCodeShouldBe(201);
    });
}
```

### 2. Write User-Centric Scenarios

Focus on **user behavior**, not implementation details:

```gherkin
# GOOD — User perspective
Scenario: Customer adds product to cart
  Given I am browsing the product catalog
  When I click "Add to Cart" for "Dog Bowl"
  Then the cart should show 1 item
  And the cart badge should update in real-time

# BAD — Implementation details
Scenario: AddToCart endpoint returns 200
  Given I POST /api/carts/items with SKU "DOG-BOWL-01"
  Then the HTTP response is 200
  And the database has a new cart_item record
  And RabbitMQ receives ItemAddedToCart message
```

### 3. Keep Steps Reusable

Write generic steps that work across scenarios:

```csharp
// GOOD — Reusable step
[Given(@"I have a product with SKU ""(.*)""")]
public void GivenIHaveAProductWithSku(string sku)
{
    _command = new AddProduct(sku, "", "", "");
}

// BAD — Scenario-specific step
[Given(@"I have a product with SKU DOG-BOWL-01 for the add product test")]
public void GivenIHaveASpecificProduct()
{
    _command = new AddProduct("DOG-BOWL-01", "", "", "");
}
```

### 4. Use Background for Common Setup

```gherkin
Feature: Cart Management

  Background:
    Given I am logged in as "alice@example.com"
    And I have an empty cart

  Scenario: Add item to cart
    # Background steps run automatically
    When I add "Dog Bowl" to my cart
    Then my cart should show 1 item

  Scenario: Remove item from cart
    # Background steps run automatically
    Given I have added "Dog Bowl" to my cart
    When I remove "Dog Bowl" from my cart
    Then my cart should be empty
```

### 5. Avoid Over-Specification

```gherkin
# GOOD — Focuses on behavior
Scenario: Cannot add duplicate product
  Given a product with SKU "DOG-BOWL-01" exists
  When I attempt to add another product with the same SKU
  Then the request should fail
  And I should see an error about duplicate SKU

# BAD — Too specific about implementation
Scenario: Cannot add duplicate product
  Given a product with SKU "DOG-BOWL-01" exists in the products table
  When I POST /api/products with SKU "DOG-BOWL-01"
  Then the response status code should be 409 Conflict
  And the response body should be {"error":"Product with SKU DOG-BOWL-01 already exists"}
  And the database transaction should be rolled back
```

### 6. Use Deterministic Test Data

```csharp
// GOOD — Well-known test data (deterministic IDs)
public static class WellKnownTestData
{
    public static class Customers
    {
        public static readonly Guid AliceId = Guid.Parse("10000000-0000-0000-0000-000000000001");
        public const string AliceEmail = "alice@example.com";
        public const string AlicePassword = "Password123!";
    }

    public static class Carts
    {
        public static readonly Guid AliceCartId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    }
}

// BAD — Random IDs (non-deterministic, breaks reproducibility)
var customerId = Guid.NewGuid();
var cartId = Guid.NewGuid();
```

### 7. Delegate Browser Logic to Page Objects

```csharp
// GOOD — Step definition delegates to Page Object
[When(@"I click ""Proceed to Checkout""")]
public async Task WhenIClickProceedToCheckout()
{
    await CartPage.ClickProceedToCheckoutAsync();
}

// Page Object encapsulates selector logic
public class CartPage
{
    public async Task ClickProceedToCheckoutAsync()
    {
        await _page.GetByTestId("proceed-to-checkout-button").ClickAsync();
    }
}

// BAD — Selector logic in step definition
[When(@"I click ""Proceed to Checkout""")]
public async Task WhenIClickProceedToCheckout()
{
    await _page.Locator("button[data-testid='proceed-to-checkout-button']").ClickAsync();
}
```

---

## Lessons Learned

These lessons are drawn from CritterSupply's actual development history (Cycles 20, 23, 26, 27).

### L1 — Reqnroll Shines for E2E, Not Alba-Only Integration Tests

**What we learned:** Reqnroll's value is highest when testing complete user journeys through the browser. For single-BC HTTP endpoint testing, Alba-only tests are more direct and maintainable.

**Evidence:** Cycle 20 (Storefront E2E) and Cycle 23 (Vendor Portal E2E) successfully used Reqnroll + Playwright for browser testing. Cycle 19.5 planning identified that `.feature` files for simple checkout API calls added overhead without benefit.

**Guidance:** If you can test it with Alba alone (no browser required), skip Reqnroll. Reserve Gherkin for user-facing scenarios that require living documentation.

**Reference:** [Cycle 19.5 Test Enhancements](../planning/cycles/cycle-19.5-test-enhancements.md)

---

### L2 — MudBlazor Component Interactions Can Fail in E2E

**What we learned:** MudBlazor's `MudSelect` dropdown component doesn't reliably open in Playwright E2E tests. The listbox popover never renders, preventing address selection in checkout flows.

**Root cause:** Blazor Server's SignalR-based rendering + headless Chrome + MudBlazor JavaScript initialization race conditions.

**Workarounds attempted (all failed):**
- JavaScript state manipulation (`_value` property injection)
- Force-click via `page.EvaluateAsync`
- Waiting for JavaScript initialization complete

**Resolution:** Mark scenarios with `@ignore` tag, test MudSelect interactions via:
1. **Alba integration tests** — HTTP-level checkout flow (no UI)
2. **bUnit component tests** — Isolated MudSelect component testing
3. **Manual testing** — Human QA verification

**Feature file documentation:**

```gherkin
# SKIPPED: MudBlazor's MudSelect dropdown doesn't work in Blazor Server + Playwright E2E environment.
# The listbox popover never opens when clicking, preventing address selection. Attempted workarounds
# (JavaScript state manipulation, force-click, waiting for JS init) all failed. The checkout workflow
# IS tested via Alba integration tests (Storefront.IntegrationTests). E2E tests focus on SignalR
# real-time updates and other browser-only behaviors.
@checkout @ignore
Scenario: Complete checkout with saved address selection
  # ...
```

**Reference:** [Cycle 20 Retrospective](../planning/cycles/cycle-20-retrospective.md), [e2e-playwright-testing.md](./e2e-playwright-testing.md)

---

### L3 — Feature Files Drive Better Collaboration (Even Without Implementation)

**What we learned:** Writing `.feature` files during planning (before implementation) forces concrete decisions on edge cases and user flows. UXE review of feature files caught gaps that would have become production bugs.

**Evidence:** Cycle 27 (Returns BC Phase 3) — UXE flagged missing exchange workflow feature file as a blocking issue. PSA created `exchange-workflow.feature` with 8 scenarios, which surfaced price difference handling ambiguities before any code was written.

**Guidance:** Write `.feature` files during planning, not after implementation. Use them as acceptance criteria during development.

**Reference:** [Cycle 27 Retrospective](../planning/cycles/cycle-27-returns-bc-phase-3-retrospective.md) (RR-4: Exchange Workflow Feature File)

---

### L4 — Tag-Based Hooks Enable Scenario-Specific Setup

**What we learned:** Using `[BeforeScenario("@tag")]` hooks allows data seeding only for scenarios that need it, reducing test execution time and cognitive overhead.

**Pattern:**

```csharp
[BeforeScenario("@checkout", Order = 5)]
public async Task SeedCheckoutScenarioData()
{
    // Only runs for @checkout scenarios
    _fixture.StubShoppingClient.SeedCart(...);
}
```

**Benefits:**
- Faster execution (no unnecessary setup for non-checkout scenarios)
- Clear intent (setup tied to specific feature)
- Reduced maintenance (change checkout data seeding in one place)

**Reference:** [Storefront.E2ETests/Hooks/DataHooks.cs](../../tests/Customer%20Experience/Storefront.E2ETests/Hooks/DataHooks.cs)

---

### L5 — Playwright Tracing is Essential for CI Failures

**What we learned:** E2E test failures in CI are impossible to diagnose without browser traces (screenshots + network logs + DOM snapshots).

**Implementation:** Always enable tracing in `[BeforeScenario]`, save traces on failure in `[AfterScenario]`:

```csharp
await _browserContext.Tracing.StartAsync(new TracingStartOptions
{
    Screenshots = true,
    Snapshots = true,
    Sources = false
});
```

**CI artifact upload:**

```yaml
- name: Upload Playwright Traces
  if: failure()
  uses: actions/upload-artifact@v4
  with:
    name: playwright-traces
    path: tests/**/playwright-traces/*.zip
```

**Reference:** [Cycle 20 Retrospective](../planning/cycles/cycle-20-retrospective.md), [ADR 0015: Playwright E2E Browser Testing](../decisions/0015-playwright-e2e-browser-testing.md)

---

### L6 — Hooks Execution Order Matters for Fixture Initialization

**What we learned:** `[BeforeScenario]` hooks must run in correct order to avoid `NullReferenceException`:
1. Fixture injection (Order=0)
2. Test data seeding (Order=5)
3. Browser context creation (Order=10)

**Pattern:**

```csharp
// FixtureHooks.cs
[BeforeScenario(Order = 0)]
public void InjectFixtureIntoScenarioContext(ScenarioContext scenarioContext)
{
    scenarioContext.Set(_fixture!, ScenarioContextKeys.Fixture);
}

// DataHooks.cs
[BeforeScenario("@checkout", Order = 5)]
public async Task SeedCheckoutScenarioData()
{
    var fixture = _scenarioContext.Get<E2ETestFixture>(ScenarioContextKeys.Fixture);
    // ...
}

// PlaywrightHooks.cs
[BeforeScenario(Order = 10)]
public async Task CreateBrowserContextAndPage()
{
    var fixture = _scenarioContext.Get<E2ETestFixture>(ScenarioContextKeys.Fixture);
    // ...
}
```

**Reference:** [Storefront.E2ETests/Hooks/](../../tests/Customer%20Experience/Storefront.E2ETests/Hooks/)

---

## Troubleshooting

### Problem: Scenarios Not Discovered

**Symptom:** `dotnet test --list-tests` doesn't show Gherkin scenarios

**Causes:**
1. Feature files not copied to test project directory
2. `Reqnroll` or `Reqnroll.xUnit` packages not installed
3. Feature files not included in build (wrong build action)

**Solutions:**
1. Ensure `.feature` files exist in test project directory tree
2. Verify `<PackageReference Include="Reqnroll" />` and `<PackageReference Include="Reqnroll.xUnit" />` in `.csproj`
3. Rebuild project: `dotnet clean && dotnet build`
4. Check feature file properties: Build Action should be `None`, Copy to Output Directory should be `Do not copy` (Reqnroll reads source files, not output)

---

### Problem: Step Definitions Not Found

**Symptom:** `No matching step definition found for one or more steps`

**Causes:**
1. Regex pattern doesn't match Gherkin step text
2. Step definition class missing `[Binding]` attribute
3. Step definition in wrong namespace or assembly
4. Build artifacts out of sync

**Solutions:**
1. Check regex pattern carefully:
   ```csharp
   // Gherkin: When I click "Proceed to Checkout"
   [When(@"I click ""(.*)""")]  // ✅ Correct
   [When(@"I click (.*)")]      // ❌ Missing quotes in regex
   ```
2. Ensure `[Binding]` attribute on step definition class:
   ```csharp
   [Binding]  // ← Required for Reqnroll discovery
   public sealed class CheckoutSteps
   ```
3. Rebuild project: `dotnet clean && dotnet build`

---

### Problem: TestFixture Not Initialized

**Symptom:** `InvalidOperationException: Fixture not initialized` or `NullReferenceException` when accessing `_fixture`

**Causes:**
1. `[BeforeTestRun]` hook not executing
2. Fixture not injected into ScenarioContext
3. Hook execution order incorrect

**Solutions:**
1. Ensure `[BeforeTestRun]` hook exists in Hooks class:
   ```csharp
   [Binding]
   public sealed class FixtureHooks
   {
       private static TestFixture? _fixture;

       [BeforeTestRun]
       public static async Task BeforeTestRun()
       {
           _fixture = new TestFixture();
           await _fixture.InitializeAsync();
       }
   }
   ```
2. Inject fixture into ScenarioContext in `[BeforeScenario]`:
   ```csharp
   [BeforeScenario(Order = 0)]
   public void InjectFixture(ScenarioContext scenarioContext)
   {
       scenarioContext.Set(_fixture!, "Fixture");
   }
   ```
3. Retrieve fixture in step definitions:
   ```csharp
   public MySteps(ScenarioContext scenarioContext)
   {
       _fixture = scenarioContext.Get<TestFixture>("Fixture");
   }
   ```

---

### Problem: Browser Traces Not Generated

**Symptom:** CI fails but no trace files uploaded

**Causes:**
1. Tracing not started in `[BeforeScenario]`
2. Trace save path incorrect
3. CI artifact upload path mismatch

**Solutions:**
1. Verify tracing started:
   ```csharp
   await _browserContext.Tracing.StartAsync(new TracingStartOptions
   {
       Screenshots = true,
       Snapshots = true
   });
   ```
2. Check trace save path in `[AfterScenario]`:
   ```csharp
   var traceDir = Path.Combine(
       Directory.GetCurrentDirectory(),
       "playwright-traces",
       $"{scenarioTitle}.zip");
   ```
3. Ensure CI artifact path matches:
   ```yaml
   path: tests/**/playwright-traces/*.zip
   ```

---

### Problem: Parallel Execution Causes Race Conditions

**Symptom:** E2E tests fail intermittently with browser context errors

**Cause:** xUnit parallelizes tests by default; E2E tests need sequential execution

**Solution:** Disable parallelization in `xunit.runner.json`:

```json
{
  "parallelizeAssembly": false,
  "parallelizeTestCollections": false
}
```

---

### Problem: Feature File Changes Not Reflected

**Symptom:** Updated Gherkin scenarios still run old behavior

**Cause:** Reqnroll generates code at build time; cached build artifacts may be stale

**Solution:**
```bash
dotnet clean
dotnet build
dotnet test
```

---

## Appendix

### A. ScenarioContext Keys Pattern

Use typed constants to avoid string typos:

```csharp
public static class ScenarioContextKeys
{
    public const string Fixture = "Fixture";
    public const string Playwright = "Playwright";
    public const string Browser = "Browser";
    public const string Page = "Page";
    public const string CartId = "CartId";
    public const string OrderId = "OrderId";
}
```

Usage:

```csharp
_scenarioContext.Set(fixture, ScenarioContextKeys.Fixture);
var fixture = _scenarioContext.Get<E2ETestFixture>(ScenarioContextKeys.Fixture);
```

---

### B. Well-Known Test Data Pattern

```csharp
public static class WellKnownTestData
{
    public static class Customers
    {
        public static readonly Guid AliceId = Guid.Parse("10000000-0000-0000-0000-000000000001");
        public const string AliceEmail = "alice@example.com";
        public const string AlicePassword = "Password123!";

        public static readonly Guid BobId = Guid.Parse("10000000-0000-0000-0000-000000000002");
        public const string BobEmail = "bob@example.com";
        public const string BobPassword = "Password123!";
    }

    public static class Carts
    {
        public static readonly Guid AliceCartId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    }

    public static class Orders
    {
        public static readonly Guid AliceOrderId = Guid.Parse("30000000-0000-0000-0000-000000000001");
    }

    public static class Products
    {
        public const string DogBowlSku = "DOG-BOWL-01";
        public const string CatToySku = "CAT-TOY-05";
    }
}
```

**Benefits:**
- Deterministic IDs prevent race conditions
- Easy to correlate test data across stubs
- No Guid collisions in concurrent tests
- Clear, searchable test data references

---

### C. Example Feature Files in CritterSupply

Explore these feature files for real-world examples:

**Customer Experience:**
- `docs/features/customer-experience/checkout-flow.feature` — 3-step checkout wizard
- `docs/features/customer-experience/cart-real-time-updates.feature` — SignalR cart badge
- `docs/features/customer-experience/storefront-protected-routes.feature` — Auth flows

**Returns BC:**
- `docs/features/returns/exchange-workflow.feature` — Exchange approval/denial
- `docs/features/returns/return-eligibility.feature` — Return window validation
- `docs/features/returns/return-inspection.feature` — Item condition assessment

**Vendor Portal:**
- `tests/Vendor Portal/VendorPortal.E2ETests/Features/vendor-auth.feature` — JWT login
- `tests/Vendor Portal/VendorPortal.E2ETests/Features/vendor-change-requests.feature` — Change request lifecycle

---

### D. Related Skills Documentation

- **[e2e-playwright-testing.md](./e2e-playwright-testing.md)** — Playwright patterns, Page Object Model, browser lifecycle
- **[critterstack-testing-patterns.md](./critterstack-testing-patterns.md)** — Alba integration tests, TestFixture patterns
- **[testcontainers-integration-tests.md](./testcontainers-integration-tests.md)** — TestContainers setup, container lifecycle
- **[bunit-component-testing.md](./bunit-component-testing.md)** — Blazor component unit testing (alternative to E2E for UI components)

---

### E. ADRs and Planning Documents

- **[ADR 0006: Reqnroll for BDD Testing](../decisions/0006-reqnroll-bdd-framework.md)** — Why Reqnroll over SpecFlow/LightBDD
- **[ADR 0015: Playwright E2E Browser Testing](../decisions/0015-playwright-e2e-browser-testing.md)** — Why Playwright over Selenium
- **[Cycle 20 Retrospective](../planning/cycles/cycle-20-retrospective.md)** — First E2E implementation (Storefront)
- **[Cycle 23 Retrospective](../planning/cycles/cycle-23-retrospective.md)** — Vendor Portal E2E testing
- **[Cycle 19.5 Test Enhancements](../planning/cycles/cycle-19.5-test-enhancements.md)** — BDD opportunities analysis

---

### F. References

- [Reqnroll Official Website](https://reqnroll.net/)
- [Reqnroll GitHub Repository](https://github.com/reqnroll/Reqnroll)
- [Gherkin Reference](https://cucumber.io/docs/gherkin/reference/)
- [Reqnroll xUnit Integration](https://docs.reqnroll.net/latest/integrations/xunit.html)
- [Cucumber Best Practices](https://cucumber.io/docs/bdd/)

---

## Summary

| Aspect | Recommendation |
|--------|----------------|
| **When to Use** | E2E browser tests, complex user flows, living documentation |
| **When NOT to Use** | Simple CRUD APIs, single-endpoint tests, pure business logic |
| **File Organization** | `.feature` files in `docs/features/`, copied to test project |
| **Integration** | Reuse TestFixture, Alba, TestContainers, Playwright infrastructure |
| **Hooks Pattern** | `[BeforeTestRun]` for fixture, `[BeforeScenario(Order)]` for browser/data |
| **Step Patterns** | Delegate to Page Objects (E2E) or Alba scenarios (integration) |
| **Tagging** | Use `@checkout`, `@signalr`, `@ignore` for organization and filtering |
| **Test Data** | Deterministic IDs via `WellKnownTestData`, tag-based seeding hooks |
| **Tracing** | Always enable Playwright tracing, save on failure for CI artifacts |
| **Best Practices** | User-centric scenarios, reusable steps, avoid over-specification |

---

*Last Updated: 2026-03-13*
*Related Cycles: 20 (Storefront E2E), 23 (Vendor Portal E2E), 26-27 (Returns BC)*
