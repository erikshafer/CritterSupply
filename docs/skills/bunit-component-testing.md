# bUnit Component Testing

> **Scope:** This skill covers unit testing Blazor components with bUnit in CritterSupply. For API-level integration tests, see `critterstack-testing-patterns.md`. For browser-level E2E tests, see `e2e-playwright-testing.md`.

Best practices for testing Blazor Server components using bUnit v2+ with MudBlazor in CritterSupply.

## When to Use bUnit vs. Playwright

| Use bUnit when…                                        | Use Playwright when…                                   |
|--------------------------------------------------------|--------------------------------------------------------|
| Testing rendering logic (conditional display, loops)   | Testing multi-step workflows (checkout wizard)         |
| Testing parameter-driven behavior                      | Testing SignalR/real-time push updates                 |
| Verifying markup structure (links, headings, labels)   | Testing JS interop heavy flows (login cookie, logout)  |
| Testing auth-gated rendering (loading vs. data states) | Testing cross-component navigation (cart → checkout)   |
| Millisecond feedback (runs in-process, no browser)     | Testing visual fidelity (screenshots, layout)          |

**Rule of thumb:** If the component's behavior depends primarily on *rendering logic and injected services*, bUnit is the right tool. If it depends on *browser APIs, JS interop, or real HTTP round-trips*, prefer Playwright.

## Testing Tools

| Tool          | Purpose                                          |
|---------------|--------------------------------------------------|
| **bUnit 2+**  | Blazor component unit testing (in-process)       |
| **xUnit**     | Test framework                                   |
| **Shouldly**  | Readable assertions                              |
| **MudBlazor** | UI component library (requires special setup)    |

## Project Setup

### Project File (`.csproj`)

bUnit projects that test MudBlazor components must use the **Razor SDK** so that `.razor` test files compile correctly (if used). Even for C#-only tests, the Razor SDK is recommended for consistency.

```xml
<Project Sdk="Microsoft.NET.Sdk.Razor">
  <PropertyGroup>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="bunit" />
    <PackageReference Include="coverlet.collector" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="Shouldly" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="path/to/YourBlazorProject.csproj" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
    <Using Include="Shouldly" />
    <Using Include="Bunit" />
  </ItemGroup>
</Project>
```

### `_Imports.razor`

Add an `_Imports.razor` to the test project root for Razor-file tests and global usings:

```razor
@using Microsoft.AspNetCore.Components.Forms
@using Microsoft.AspNetCore.Components.Web
@using Microsoft.JSInterop
@using Microsoft.Extensions.DependencyInjection
@using AngleSharp.Dom
@using Bunit
@using Bunit.TestDoubles
@using Xunit
@using Shouldly
```

## MudBlazor Setup (Critical)

MudBlazor v9+ requires specific setup in bUnit tests. Without this, components using MudBlazor controls will throw `MissingServiceException` or `MudPopoverProvider` errors.

### Base Test Class Pattern

```csharp
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;

public abstract class BunitTestBase : BunitContext, IAsyncLifetime
{
    protected BunitTestBase()
    {
        // MudBlazor components use internal JS interop — loose mode
        // prevents bUnit from throwing on unhandled JS calls.
        JSInterop.Mode = JSRuntimeMode.Loose;

        // Register all MudBlazor services (ISnackbar, IDialogService, etc.)
        Services.AddMudServices();
    }

    // IAsyncLifetime ensures MudBlazor's IAsyncDisposable services
    // (like PointerEventsNoneService) are disposed correctly.
    // Without this, xUnit's synchronous Dispose throws InvalidOperationException.
    public Task InitializeAsync() => Task.CompletedTask;

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
    }

    /// <summary>
    /// Renders a component with MudPopoverProvider pre-rendered.
    /// Required for components using MudSelect, MudMenu, MudTable,
    /// MudAutocomplete, or any popover-based MudBlazor control.
    /// </summary>
    protected IRenderedComponent<TComponent> RenderWithMud<TComponent>(
        Action<ComponentParameterCollectionBuilder<TComponent>>? parameterBuilder = null)
        where TComponent : IComponent
    {
        Render<MudPopoverProvider>();

        return parameterBuilder is null
            ? Render<TComponent>()
            : Render<TComponent>(parameterBuilder);
    }
}
```

### Key MudBlazor + bUnit Gotchas

1. **`IAsyncLifetime` is mandatory** — MudBlazor 9+ registers services that only implement `IAsyncDisposable`. Without implementing `IAsyncLifetime` on the test class, xUnit's synchronous `Dispose()` throws `InvalidOperationException`.

2. **`JSInterop.Mode = JSRuntimeMode.Loose`** — MudBlazor components make many internal JS interop calls. Strict mode (bUnit's default) throws on any unhandled call.

3. **`MudPopoverProvider` must be pre-rendered** — Components using `MudSelect`, `MudMenu`, `MudTable`, `MudAutocomplete`, etc. require a `MudPopoverProvider` in the render tree. The `RenderWithMud<T>()` helper method handles this. **Do NOT** use `RenderTree.TryAdd<MudPopoverProvider>()` — that component doesn't have a `ChildContent` parameter and will throw `ArgumentException`. (**M34.0 note:** This also applies to E2E — the root `App.razor` must include `<MudPopoverProvider />` or pages with popover components will trigger the `blazor-error-ui` overlay. M34.0 found this missing in `VendorPortal.Web/App.razor`.)

4. **Use `Render<T>()` for simple components** — Components that only use basic MudBlazor controls (MudText, MudButton, MudIcon, MudGrid, MudPaper, MudAlert) work fine with plain `Render<T>()` — no `MudPopoverProvider` needed.

5. **Currency formatting is locale-dependent** — `ToString("C")` produces `$129.99` in `en-US` but `¤129.99` in `C.UTF-8` (common in CI). Assert on the numeric portion (e.g., `"129.99"`) rather than the formatted string.

6. **`MudSelect` with `Required="true"` and a default value** ⭐ *M34.0 Addition* — `MudForm`'s `@bind-IsValid` does **not** consider a programmatically-set default value as "validated." A `MudSelect` initialized with `_type = "Description"` but marked `Required="true"` will never pass validation until the user explicitly interacts with the component — leaving the submit button permanently disabled. **Fix:** Remove `Required="true"` from `MudSelect` fields that have a sensible default value and can never be empty. Only use `Required` on fields that start empty and need user input.

### MudBlazor v9+ Type Parameters (M32.1)

**CRITICAL DISCOVERY (M32.1 Session 6):** MudBlazor v9+ is **generic-first** — many components that worked without explicit type parameters in v8 now require them in v9+.

**Key Affected Components:**
- `MudList` + `MudListItem`
- `MudSelect` + `MudSelectItem`
- `MudTable`
- `MudDataGrid`
- `MudTreeView` + `MudTreeViewItem`

**Pattern:**

```razor
<!-- ❌ WRONG (v8 syntax - throws compilation error in v9) -->
<MudList>
    <MudListItem Icon="@Icons.Material.Filled.Home">
        Home
    </MudListItem>
</MudList>

<!-- ✅ CORRECT (v9+ syntax - explicit type parameters) -->
<MudList T="string">
    <MudListItem T="string" Icon="@Icons.Material.Filled.Home">
        Home
    </MudListItem>
</MudList>
```

**Why This Matters:**
- MudBlazor v9+ refactored components to be generic-first for better data binding
- Type inference fails for non-data-bound lists (static text items)
- **Every nested item** needs the same type parameter as its parent
- Forgetting type parameters causes compilation errors or runtime exceptions

**Real-World Example from M32.1 (Backoffice.Web Dashboard):**

```razor
<!-- Navigation sidebar with icon list -->
<MudList T="string" Clickable="true" Color="Color.Primary">
    <MudListItem T="string" Icon="@Icons.Material.Filled.Dashboard" Href="/">
        Dashboard
    </MudListItem>
    <MudListItem T="string" Icon="@Icons.Material.Filled.ShoppingCart" Href="/orders">
        Orders
    </MudListItem>
    <MudListItem T="string" Icon="@Icons.Material.Filled.Inventory" Href="/catalog">
        Product Catalog
    </MudListItem>
    <MudListItem T="string" Icon="@Icons.Material.Filled.LocalShipping" Href="/fulfillment">
        Fulfillment
    </MudListItem>
    <MudListItem T="string" Icon="@Icons.Material.Filled.Headset" Href="/customer-service">
        Customer Service
    </MudListItem>
</MudList>
```

**Testing Pattern:**

```csharp
[Fact]
public void NavMenu_RendersAllLinks()
{
    // Component uses <MudList T="string"><MudListItem T="string">...
    var cut = RenderWithMud<NavMenu>();

    var links = cut.FindAll("a.mud-nav-link");
    links.Count.ShouldBe(5);

    var hrefs = links.Select(l => l.GetAttribute("href")).ToArray();
    hrefs.ShouldContain("/");
    hrefs.ShouldContain("/orders");
    hrefs.ShouldContain("/catalog");
}
```

**Common Type Parameters:**

| Component Use Case | Type Parameter | Example |
|--------------------|----------------|---------|
| **Static text list** | `T="string"` | Navigation menu, quick links |
| **Data-bound list** | `T="TModel"` | Product list (`T="Product"`), order list (`T="Order"`) |
| **String dropdown** | `T="string"` | Category filter, status selector |
| **Enum dropdown** | `T="OrderStatus"` | Status filter, role selector |
| **Entity dropdown** | `T="Product"` | Product picker with `ToStringFunc` |

**Decision Matrix:**

| Component Renders | Use Type Parameter | Example |
|-------------------|-------------------|---------|
| Static text (no data binding) | `T="string"` | `<MudList T="string">` with hardcoded `<MudListItem T="string">` |
| Collection of entities | `T="TEntity"` | `<MudTable T="Order" Items="@orders">` |
| Enum values | `T="TEnum"` | `<MudSelect T="OrderStatus" @bind-Value="filter">` |

**Anti-Pattern — Forgetting Type Parameters:**

```razor
<!-- ❌ Compilation error in v9+ -->
<MudList>
    <MudListItem>Home</MudListItem>
</MudList>

<!-- Compiler error message:
CS1503: Argument 1: cannot convert from 'method group' to '...'
OR
CS0411: The type arguments for method '...' cannot be inferred from the usage
-->
```

**Migration Checklist (v8 → v9+):**

- [ ] Search codebase for `<MudList>` — add `T="string"` to all occurrences
- [ ] Search for `<MudListItem>` — add `T="string"` to all occurrences
- [ ] Search for `<MudSelect>` — verify explicit `T="{Type}"` exists
- [ ] Search for `<MudTable>` — verify `Items="@collection"` has matching `T="{Type}"`
- [ ] Run tests — type parameter mismatches cause runtime failures
- [ ] Check for nested generics — `MudList<T>` inside `MudExpansionPanel` needs careful nesting

**Reference:** [M32.1 Retrospective - Session 6 Discovery D1](../../planning/milestones/m32.1-retrospective.md)

---

## Writing Tests

### Rendering Simple Components

For components with no service dependencies (pure presentation):

```csharp
public sealed class HomeTests : BunitTestBase
{
    [Fact]
    public void Home_RendersHeroBanner()
    {
        var cut = Render<Home>();

        cut.Markup.ShouldContain("Everything Your Pet Needs");
    }

    [Fact]
    public void Home_QuickLinks_HaveCorrectHrefs()
    {
        var cut = Render<Home>();

        var links = cut.FindAll("a[href]");
        var hrefs = links.Select(l => l.GetAttribute("href")).ToList();

        hrefs.ShouldContain("/products");
        hrefs.ShouldContain("/cart");
    }
}
```

### Testing Click Interactions

```csharp
[Fact]
public void Counter_ClickButton_IncrementsCount()
{
    var cut = Render<Counter>();

    cut.Find("button").Click();

    cut.Find("p[role='status']").TextContent.ShouldContain("Current count: 1");
}
```

### Emulating Authentication State

bUnit v2 provides `AddAuthorization()` on `BunitContext` for testing `[Authorize]`-gated components and `<AuthorizeView>`:

```csharp
public sealed class AccountTests : BunitTestBase
{
    [Fact]
    public void Account_WhenAuthenticated_RendersCustomerInfo()
    {
        var authContext = this.AddAuthorization();
        authContext.SetAuthorized("alice@critter.test");
        authContext.SetClaims(
            new Claim("CustomerId", "a1b2c3d4-..."),
            new Claim(ClaimTypes.Email, "alice@critter.test"),
            new Claim(ClaimTypes.GivenName, "Alice"),
            new Claim(ClaimTypes.Surname, "Wonder")
        );

        var cut = Render<Account>();

        cut.Markup.ShouldContain("Alice");
        cut.Markup.ShouldContain("alice@critter.test");
    }

    [Fact]
    public void Account_WhenNotAuthenticated_ShowsLoadingState()
    {
        var authContext = this.AddAuthorization();
        authContext.SetNotAuthorized();

        var cut = Render<Account>();

        cut.Markup.ShouldContain("Loading account information");
    }
}
```

> **Note:** In bUnit v2, use `this.AddAuthorization()` (not `AddTestAuthorization()` from v1).

> **Important caveat:** `AddAuthorization()` is a good fit for simple `[Authorize]` or role-based rendering checks. For pages that depend on `AuthorizeView Policy=...`, redirects, auth state propagation, or MudBlazor-heavy interaction after login, prefer Playwright E2E tests. M33.0 showed that policy-gated Backoffice pages were much more stable and representative at the browser level than in bUnit.

### Mocking HttpClient / IHttpClientFactory

Components that call APIs via `IHttpClientFactory` need a mock. Create a reusable `MockHttpMessageHandler`:

```csharp
public sealed class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Dictionary<string, Func<HttpResponseMessage>> _responses = new();

    public void SetResponse<T>(string pathPrefix, T content)
    {
        _responses[pathPrefix] = () => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(content, options: new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            })
        };
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.PathAndQuery ?? "";
        foreach (var (prefix, factory) in _responses)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return factory();
        }
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }
}

public sealed class MockHttpClientFactory(MockHttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) =>
        new(handler) { BaseAddress = new Uri("http://localhost:5237") };
}
```

Usage in tests:

```csharp
public sealed class ProductsTests : BunitTestBase
{
    private readonly MockHttpMessageHandler _mockHandler = new();

    public ProductsTests()
    {
        this.AddAuthorization().SetNotAuthorized();
        Services.AddSingleton<IHttpClientFactory>(new MockHttpClientFactory(_mockHandler));
    }

    [Fact]
    public void Products_WhenNoProducts_ShowsEmptyMessage()
    {
        _mockHandler.SetResponse("/api/storefront/products",
            new { Products = Array.Empty<object>(), TotalCount = 0, Page = 1, PageSize = 20 });

        var cut = RenderWithMud<Products>();
        cut.WaitForAssertion(() =>
        {
            cut.Markup.ShouldContain("No products found");
        });
    }
}
```

### Async Data Loading and `WaitForAssertion`

Components that load data in `OnInitializedAsync` need `WaitForAssertion` to wait for the async render cycle:

```csharp
[Fact]
public void Products_WhenProductsExist_RendersProductCards()
{
    _mockHandler.SetResponse("/api/storefront/products", testData);

    var cut = RenderWithMud<Products>();

    // WaitForAssertion retries until assertion passes or timeout expires
    cut.WaitForAssertion(() =>
    {
        cut.Markup.ShouldContain("Premium Dog Food");
    });
}
```

### Components Using MudTable

`MudTable` requires `MudPopoverProvider`. Use `RenderWithMud<T>()`:

```csharp
[Fact]
public void OrderHistory_RendersTableHeaders()
{
    var cut = RenderWithMud<OrderHistory>();

    cut.Markup.ShouldContain("Order ID");
    cut.Markup.ShouldContain("Status");
}
```

## What NOT to Test with bUnit

These components/behaviors are better tested with Playwright E2E:

| Component/Feature          | Reason bUnit is inadequate                           |
|----------------------------|------------------------------------------------------|
| **Checkout.razor**         | MudStepper has heavy JS interop; step navigation     |
| **Cart.razor SignalR**     | Real WebSocket subscription, `[JSInvokable]` updates |
| **InteractiveAppBar.razor**| SignalR subscription, logout JS interop               |
| **Login.razor submit**     | `authHelper.login` JS call sets browser cookie       |
| **ReconnectModal.razor**   | Pure JS interop component                            |
| **Navigation flows**       | Full-page navigation with `forceLoad: true`          |

**Rule:** If a component's critical behavior flows through `IJSRuntime`, test it with Playwright.

**Additional rule:** If a page is policy-gated, redirect-heavy, or depends on browser-auth state over multiple renders, start with Playwright unless you only need to verify a tiny isolated rendering branch.

## Test Organization

Mirror the source project structure:

```
tests/Customer Experience/Storefront.Web.Tests/
├── BunitTestBase.cs                      # Shared base class
├── _Imports.razor                        # Global usings for .razor tests
├── Storefront.Web.Tests.csproj
├── Components/
│   ├── Pages/
│   │   ├── CounterTests.cs
│   │   ├── HomeTests.cs
│   │   ├── NotFoundTests.cs
│   │   ├── LoginTests.cs
│   │   ├── AccountTests.cs
│   │   ├── OrderHistoryTests.cs
│   │   └── ProductsTests.cs
│   └── Layout/
│       └── (future layout component tests)
```

## Naming Conventions

Follow the existing CritterSupply pattern: `ComponentName_Scenario_ExpectedBehavior`

```
Counter_ClickButton_IncrementsCount
Home_RendersFourTrustItems
Account_WhenNotAuthenticated_ShowsLoadingState
Products_OutOfStockItem_AddToCartButtonIsDisabled
OrderHistory_RendersThreeHardcodedOrders
```

## CI Integration

bUnit tests run as part of the standard `dotnet test` pipeline — no browser installation or Docker needed:

```bash
dotnet test "tests/Customer Experience/Storefront.Web.Tests/Storefront.Web.Tests.csproj"
```

Typical execution: **< 2 seconds** for 40+ tests (vs. 30+ seconds for Playwright E2E).

## Checklist: Adding a New bUnit Test

1. Identify the component's dependencies (services, auth, JS interop)
2. Choose `Render<T>()` (simple) or `RenderWithMud<T>()` (popover-dependent)
3. Register required services (`IHttpClientFactory`, auth, `IConfiguration`)
4. Set up JSInterop expectations if needed (usually loose mode handles this)
5. Use `WaitForAssertion()` for components with async data loading
6. Assert on markup content, not exact HTML structure (MudBlazor generates complex markup)
7. Avoid currency-symbol assertions — use numeric values only

## References

- [bUnit Documentation](https://bunit.dev/docs/getting-started)
- [bUnit v2 Migration Guide](https://bunit.dev/docs/migrations)
- [MudBlazor Documentation](https://mudblazor.com)
- [CritterSupply E2E Testing](e2e-playwright-testing.md) — Playwright patterns for full browser tests
- [CritterSupply Testing Patterns](critterstack-testing-patterns.md) — Alba integration test patterns
