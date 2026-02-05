# Reqnroll BDD Testing

Patterns for writing behavior-driven development (BDD) tests using Reqnroll and Gherkin in CritterSupply.

## When to Use This Skill

Use this skill when:
- Writing user-facing integration tests with Gherkin specifications
- Creating living documentation for complex user flows
- Collaborating with non-technical stakeholders on requirements
- Testing features in the Customer Experience BC (or other user-facing BCs)
- Verifying end-to-end scenarios (checkout, order placement, cart management)

## Core Principles

1. **Reserve BDD for High-Value Scenarios** — Use Reqnroll for complex user flows, keep simple CRUD tests as Alba-only
2. **Gherkin for Behavior, Not Implementation** — Focus on user actions and outcomes, not internal mechanics
3. **Living Documentation** — `.feature` files serve as up-to-date specifications
4. **Integration with TestFixture** — Reqnroll tests use the same Alba + TestContainers infrastructure

## Gherkin Feature Files

### File Location

Feature files live in `docs/features/` organized by bounded context:

```
docs/features/
├── product-catalog/
│   └── add-product.feature
├── customer-experience/
│   ├── cart-real-time-updates.feature
│   ├── checkout-flow.feature
│   └── product-browsing.feature
└── orders/
    └── order-saga-orchestration.feature
```

### Gherkin Syntax

```gherkin
Feature: Add Product to Catalog
  As a catalog administrator
  I want to add new products to the catalog
  So that customers can browse and purchase them

  Background:
    Given the product catalog is empty

  Scenario: Add a valid product
    Given I have a product with SKU "DOG-BOWL-01"
    And the product name is "Ceramic Dog Bowl"
    And the product category is "Dogs"
    And the product description is "A durable ceramic bowl for dogs"
    When I add the product to the catalog
    Then the product should be successfully created
    And the product should be retrievable by SKU "DOG-BOWL-01"
    And the product status should be "Active"

  Scenario: Cannot add product with duplicate SKU
    Given a product with SKU "DOG-BOWL-01" already exists
    When I attempt to add another product with SKU "DOG-BOWL-01"
    Then the request should fail with status code 409
    And the error message should indicate "Product with SKU already exists"
```

**Key elements:**
- **Feature** — High-level description of the capability
- **Background** — Setup steps run before each scenario
- **Scenario** — Specific test case with Given/When/Then steps
- **Tables** — Structured data for complex inputs (see below)

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

> **Reference:** [Gherkin Syntax Documentation](https://cucumber.io/docs/gherkin/reference/)

## Step Definitions

Step definitions are C# methods that implement Gherkin steps. They use `[Binding]` and regex attributes.

### Basic Step Definition Pattern

```csharp
using Marten;
using ProductCatalog.Api.Products;
using ProductCatalog.Products;
using Reqnroll;
using Shouldly;

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
        _fixture = Hooks.GetTestFixture();
        _scenarioContext = scenarioContext;
    }

    [Given(@"the product catalog is empty")]
    public async Task GivenTheProductCatalogIsEmpty()
    {
        await _fixture.CleanAllDocumentsAsync();
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

All attributes use regex patterns for parameter extraction:
- `"(.*)"` captures any string
- `(.*)` captures unquoted values (numbers, booleans)
- `"([^"]*)"` captures strings (prevents greedy matching)

### Sharing State with ScenarioContext

```csharp
// Store data in one step
[When(@"I add the product to the catalog")]
public async Task WhenIAddTheProductToTheCatalog()
{
    // ...
    _scenarioContext["sku"] = _command!.Sku;
}

// Retrieve data in another step
[Then(@"the product status should be ""(.*)""")]
public async Task ThenTheProductStatusShouldBe(string expectedStatus)
{
    var sku = _scenarioContext.Get<string>("sku");

    using var session = _fixture.GetDocumentSession();
    var product = await session.LoadAsync<Product>(sku);

    product.Status.ToString().ShouldBe(expectedStatus);
}
```

### Handling Tables

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

## Integration with TestFixture

Reqnroll tests reuse the same `TestFixture` infrastructure as Alba integration tests.

### Hooks File

Create a `Hooks.cs` file to initialize the TestFixture once for all scenarios:

```csharp
using Reqnroll;

namespace ProductCatalog.Api.IntegrationTests;

[Binding]
public sealed class Hooks
{
    private static TestFixture? _testFixture;

    [BeforeTestRun]
    public static async Task BeforeTestRun()
    {
        _testFixture = new TestFixture();
        await _testFixture.InitializeAsync();
    }

    [AfterTestRun]
    public static async Task AfterTestRun()
    {
        if (_testFixture != null)
        {
            await _testFixture.DisposeAsync();
        }
    }

    [BeforeScenario]
    public void BeforeScenario()
    {
        if (_testFixture == null)
        {
            throw new InvalidOperationException("TestFixture not initialized");
        }
    }

    public static TestFixture GetTestFixture()
    {
        if (_testFixture == null)
        {
            throw new InvalidOperationException("TestFixture not initialized. Ensure [BeforeTestRun] hook has executed.");
        }

        return _testFixture;
    }
}
```

**Hook lifecycle:**
- `[BeforeTestRun]` — Runs once before all scenarios (initialize TestFixture)
- `[AfterTestRun]` — Runs once after all scenarios (dispose TestFixture)
- `[BeforeScenario]` — Runs before each scenario (validation)
- `[AfterScenario]` — Runs after each scenario (cleanup if needed)

### Accessing TestFixture in Steps

```csharp
public AddProductSteps(ScenarioContext scenarioContext)
{
    _fixture = Hooks.GetTestFixture();
    _scenarioContext = scenarioContext;
}
```

**Benefits of this pattern:**
- TestFixture initialized once (faster test execution)
- TestContainers shared across all scenarios
- Alba Host reused (no repeated startup cost)
- Clean separation between Reqnroll and Alba infrastructure

## Project Configuration

### Required NuGet Packages

```xml
<ItemGroup>
  <PackageReference Include="Reqnroll" />
  <PackageReference Include="Reqnroll.xUnit" />
  <PackageReference Include="Alba" />
  <PackageReference Include="Testcontainers.PostgreSql" />
  <PackageReference Include="Shouldly" />
  <PackageReference Include="xunit" />
</ItemGroup>
```

### Feature File Inclusion

Feature files must be copied to the test project and included in the build:

```bash
# Copy from docs/features/ to test project
cp docs/features/product-catalog/add-product.feature \
   tests/Product\ Catalog/ProductCatalog.Api.IntegrationTests/AddProduct.feature
```

Reqnroll auto-generates test code from `.feature` files at build time.

### Reqnroll Configuration (Optional)

Create `reqnroll.json` in the test project root:

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

## Running Tests

```bash
# Run all Reqnroll scenarios
dotnet test --filter "DisplayName~Scenario"

# Run specific feature
dotnet test --filter "DisplayName~Add a valid product"

# List all discovered scenarios
dotnet test --list-tests
```

Reqnroll generates xUnit test methods from Gherkin scenarios, so they appear alongside regular xUnit tests.

## Best Practices

### 1. Reserve BDD for High-Value Scenarios

**Use Reqnroll for:**
- Complex user flows (checkout, order placement, cart management)
- Multi-step workflows with business rules
- Scenarios requiring non-technical stakeholder validation

**Use Alba-only for:**
- Simple CRUD operations
- Single-endpoint tests
- Internal API testing

### 2. Write User-Centric Scenarios

```gherkin
# GOOD — User perspective
Scenario: Customer adds product to cart
  Given I am browsing the product catalog
  When I click "Add to Cart" for "Dog Bowl"
  Then the cart should show 1 item
  And the cart total should be $19.99

# BAD — Implementation details
Scenario: AddToCart endpoint returns 200
  Given I POST /api/carts/items with SKU "DOG-BOWL-01"
  Then the HTTP response is 200
  And the database has a new cart_item record
```

### 3. Keep Steps Reusable

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
Feature: Product Catalog Management

  Background:
    Given I am logged in as a catalog administrator
    And the product catalog is empty

  Scenario: Add product
    # Background steps run automatically
    When I add a new product
    Then it should appear in the catalog
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
```

## Living Documentation

Reqnroll scenarios serve as living documentation because:
1. **Always up-to-date** — Tests fail if behavior diverges from specification
2. **Human-readable** — Non-technical stakeholders can read and validate
3. **Executable** — Specifications are also automated tests
4. **Version-controlled** — Track changes to requirements over time

## Troubleshooting

### Scenarios Not Discovered

**Problem:** `dotnet test --list-tests` doesn't show Gherkin scenarios

**Solution:**
- Ensure `.feature` files are copied to test project
- Check that `Reqnroll` and `Reqnroll.xUnit` packages are installed
- Rebuild project (`dotnet clean && dotnet build`)

### TestFixture Not Initialized

**Problem:** `InvalidOperationException: TestFixture not initialized`

**Solution:**
- Ensure `Hooks.cs` has `[BeforeTestRun]` method
- Check that step definitions use `Hooks.GetTestFixture()`
- Verify `[Binding]` attribute is on `Hooks` class

### Step Definitions Not Found

**Problem:** `No matching step definition found for one or more steps`

**Solution:**
- Check regex patterns in `[Given]`, `[When]`, `[Then]` attributes
- Ensure step definition class has `[Binding]` attribute
- Rebuild project to regenerate binding metadata

## Summary

| Aspect | Recommendation |
|--------|----------------|
| **When to Use** | Complex user flows, living documentation, stakeholder collaboration |
| **File Organization** | `.feature` files in `docs/features/`, step definitions in test project |
| **Integration** | Reuse `TestFixture`, Alba, TestContainers infrastructure |
| **Step Patterns** | Use regex for parameter extraction, `ScenarioContext` for state sharing |
| **Best Practices** | User-centric scenarios, reusable steps, avoid over-specification |
| **Test Execution** | Reqnroll generates xUnit tests, run with `dotnet test` |

## References

- [Reqnroll Official Documentation](https://reqnroll.net/)
- [Gherkin Reference](https://cucumber.io/docs/gherkin/reference/)
- [ADR 0005: Reqnroll BDD Framework](../docs/decisions/0005-reqnroll-bdd-framework.md)
- [CritterSupply Feature Files](../docs/features/)
- [critterstack-testing-patterns.md](./critterstack-testing-patterns.md) — Alba integration testing patterns
