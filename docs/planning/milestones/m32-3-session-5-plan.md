# M32.3 Session 5 Plan: E2E Tests for Warehouse Admin + Pricing Admin

**Date:** 2026-03-20
**Milestone:** M32.3 — Backoffice Phase 3B: Write Operations Depth
**Session:** 5 of 10
**Goal:** Implement E2E tests for Warehouse Admin and Pricing Admin workflows

---

## Executive Summary

This session implements **E2E test coverage** for two write-operations workflows completed in Sessions 3-4:
1. **Pricing Admin** (PriceEdit.razor from Session 3)
2. **Warehouse Admin** (InventoryList.razor + InventoryEdit.razor from Session 4)

**Pattern:** Follows established E2E patterns from M32.1-M32.2 (Reqnroll + Playwright + Page Object Model).

---

## Prerequisites Verified

### From Session 3 & 4 Analysis

✅ **Pricing Admin UI exists** (Session 3):
- `src/Backoffice/Backoffice.Web/Pages/Products/PriceEdit.razor`
- Route: `/products/{sku}/price`
- Authorization: `pricing-manager, system-admin`
- Features: Set base price, floor/ceiling constraints, success/error messages

✅ **Warehouse Admin UI exists** (Session 4):
- `src/Backoffice/Backoffice.Web/Pages/Inventory/InventoryList.razor`
- `src/Backoffice/Backoffice.Web/Pages/Inventory/InventoryEdit.razor`
- Routes: `/inventory` and `/inventory/{sku}/edit`
- Authorization: `warehouse-clerk, system-admin`
- Features: Browse inventory, adjust inventory (±), receive inbound stock

✅ **E2E test infrastructure exists** (M32.1 Sessions 9-10):
- `tests/Backoffice/Backoffice.E2ETests/` project
- 3-server WASM fixture (BackofficeIdentity.Api + Backoffice.Api + Backoffice.Web)
- Playwright configuration with tracing enabled
- Existing Page Object Models: LoginPage, DashboardPage, CustomerSearchPage, OperationsAlertsPage
- Existing step definitions: AuthenticationSteps, AuthorizationSteps, SessionExpirySteps

✅ **Stub clients support write operations** (Session 3-4):
- `StubPricingClient` with `SetBasePriceAsync` method
- `StubInventoryClient` with `AdjustInventoryAsync` and `ReceiveInboundStockAsync` methods
- `StubCatalogClient` with product data (DEMO-001, DEMO-002, etc.)

---

## Session Objectives

### Primary Deliverables

1. **Create PricingAdmin.feature:**
   - 6-8 scenarios covering:
     - PricingManager can set base price
     - Validation: price must be > $0.00
     - Floor/ceiling constraint enforcement
     - Success message after setting price
     - Session-expired handling
     - SystemAdmin can also access (RBAC verification)

2. **Create PriceEditPage.cs Page Object Model:**
   - Locators for: SKU header, current price display, price input, submit button, success/error messages
   - Methods: `NavigateTo(sku)`, `GetCurrentPrice()`, `SetPrice(amount)`, `SubmitPrice()`, `GetSuccessMessage()`, `GetErrorMessage()`

3. **Create PricingAdminSteps.cs step definitions:**
   - Given: Admin navigates to price edit page
   - When: Admin sets price to $X.XX
   - Then: Price is updated successfully
   - Then: Error message appears (validation failure)

4. **Create WarehouseAdmin.feature:**
   - 8-10 scenarios covering:
     - Browse inventory list
     - Filter inventory by SKU
     - Adjust inventory (positive and negative)
     - Receive inbound stock
     - Validation: Adjust quantity != 0, Receive quantity > 0, Reason required
     - Success messages after operations
     - Session-expired handling
     - SystemAdmin can also access (RBAC verification)

5. **Create InventoryListPage.cs Page Object Model:**
   - Locators for: search input, table rows, SKU cells, stock status chips
   - Methods: `NavigateTo()`, `SearchBySku(sku)`, `ClickRow(sku)`, `GetStockStatus(sku)`

6. **Create InventoryEditPage.cs Page Object Model:**
   - Locators for: Available/Reserved/Total KPI cards, Adjust form inputs, Receive form inputs, submit buttons, success/error messages
   - Methods: `NavigateTo(sku)`, `GetAvailableQuantity()`, `AdjustInventory(quantity, reason, adjustedBy)`, `ReceiveStock(quantity, source)`, `GetSuccessMessage()`, `GetErrorMessage()`

7. **Create WarehouseAdminSteps.cs step definitions:**
   - Given: Admin navigates to inventory list/edit page
   - When: Admin adjusts inventory by X units
   - When: Admin receives Y units from supplier Z
   - Then: Stock levels update successfully
   - Then: Success/error messages appear

### Out of Scope (Deferred to Session 6+)

- User Management write UI (deferred to Session 6)
- CSV/Excel exports (deferred to Session 7)
- Bulk operations pattern (deferred to Session 8)
- Cross-role comprehensive smoke tests (deferred to Session 9)

---

## Implementation Plan

### Phase 1: Pricing Admin E2E Tests (~60 min)

**1.1 Create PricingAdmin.feature** (`tests/Backoffice/Backoffice.E2ETests/Features/PricingAdmin.feature`):

```gherkin
Feature: Pricing Admin
  As a Pricing Manager
  I want to manage product prices
  So that I can maintain competitive pricing

  Background:
    Given the Backoffice system is running
    And stub catalog client has product "DEMO-001" with name "Cat Food Premium"
    And stub pricing client has product "DEMO-001" with current price "$19.99"

  Scenario: Pricing Manager can set base price
    Given admin user exists with email "pricing@example.com" and role "PricingManager"
    When I log in with email "pricing@example.com" and password "password123"
    And I navigate to the price edit page for SKU "DEMO-001"
    Then I should see the current price "$19.99"
    When I set the price to "$24.99"
    And I submit the price change
    Then I should see the success message "Price updated successfully"
    And the current price should be "$24.99"

  Scenario: Price must be greater than zero
    Given admin user exists with email "pricing@example.com" and role "PricingManager"
    When I log in with email "pricing@example.com" and password "password123"
    And I navigate to the price edit page for SKU "DEMO-001"
    And I set the price to "$0.00"
    Then the submit button should be disabled

  Scenario: Floor price constraint is enforced
    Given admin user exists with email "pricing@example.com" and role "PricingManager"
    And stub pricing client has floor price "$15.00" for SKU "DEMO-001"
    When I log in with email "pricing@example.com" and password "password123"
    And I navigate to the price edit page for SKU "DEMO-001"
    And I set the price to "$10.00"
    And I submit the price change
    Then I should see the error message "Price cannot be below floor price of $15.00"

  Scenario: Ceiling price constraint is enforced
    Given admin user exists with email "pricing@example.com" and role "PricingManager"
    And stub pricing client has ceiling price "$30.00" for SKU "DEMO-001"
    When I log in with email "pricing@example.com" and password "password123"
    And I navigate to the price edit page for SKU "DEMO-001"
    And I set the price to "$35.00"
    And I submit the price change
    Then I should see the error message "Price cannot exceed ceiling price of $30.00"

  Scenario: Session expired redirects to login
    Given admin user exists with email "pricing@example.com" and role "PricingManager"
    When I log in with email "pricing@example.com" and password "password123"
    And I navigate to the price edit page for SKU "DEMO-001"
    And my session expires
    And I set the price to "$24.99"
    And I submit the price change
    Then I should be redirected to the login page

  Scenario: SystemAdmin can set prices
    Given admin user exists with email "admin@example.com" and role "SystemAdmin"
    When I log in with email "admin@example.com" and password "password123"
    And I navigate to the price edit page for SKU "DEMO-001"
    Then I should see the price edit form
```

**1.2 Create PriceEditPage.cs** (`tests/Backoffice/Backoffice.E2ETests/PageObjects/PriceEditPage.cs`):

```csharp
namespace Backoffice.E2ETests.PageObjects;

public sealed class PriceEditPage
{
    private readonly IPage _page;

    public PriceEditPage(IPage page) => _page = page;

    public async Task NavigateToAsync(string sku)
    {
        await _page.GotoAsync($"/products/{Uri.EscapeDataString(sku)}/price");
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    public async Task<string> GetCurrentPriceAsync()
    {
        var element = _page.GetByTestId("current-price");
        return await element.InnerTextAsync();
    }

    public async Task SetPriceAsync(string price)
    {
        var input = _page.GetByTestId("price-input");
        await input.FillAsync(price.Replace("$", ""));
    }

    public async Task SubmitPriceAsync()
    {
        var button = _page.GetByTestId("submit-price-button");
        await button.ClickAsync();
        await _page.WaitForTimeoutAsync(500); // Allow backend processing
    }

    public async Task<string> GetSuccessMessageAsync()
    {
        var element = _page.GetByTestId("success-message");
        return await element.InnerTextAsync();
    }

    public async Task<string> GetErrorMessageAsync()
    {
        var element = _page.GetByTestId("error-message");
        return await element.InnerTextAsync();
    }

    public async Task<bool> IsSubmitButtonDisabledAsync()
    {
        var button = _page.GetByTestId("submit-price-button");
        return await button.IsDisabledAsync();
    }

    public async Task<bool> IsPriceEditFormVisibleAsync()
    {
        var form = _page.GetByTestId("price-edit-form");
        return await form.IsVisibleAsync();
    }
}
```

**1.3 Create PricingAdminSteps.cs** (`tests/Backoffice/Backoffice.E2ETests/StepDefinitions/PricingAdminSteps.cs`):

```csharp
namespace Backoffice.E2ETests.StepDefinitions;

[Binding]
public sealed class PricingAdminSteps
{
    private readonly E2ETestFixture _fixture;
    private readonly IPage _page;
    private PriceEditPage _priceEditPage = null!;

    public PricingAdminSteps(E2ETestFixture fixture, IPage page)
    {
        _fixture = fixture;
        _page = page;
    }

    [Given(@"stub pricing client has product ""(.*)"" with current price ""(.*)""")]
    public void GivenStubPricingClientHasProductWithCurrentPrice(string sku, string price)
    {
        var decimalPrice = decimal.Parse(price.Replace("$", ""));
        _fixture.StubPricingClient.SetCurrentPrice(sku, decimalPrice);
    }

    [Given(@"stub pricing client has floor price ""(.*)"" for SKU ""(.*)""")]
    public void GivenStubPricingClientHasFloorPrice(string price, string sku)
    {
        var decimalPrice = decimal.Parse(price.Replace("$", ""));
        _fixture.StubPricingClient.SetFloorPrice(sku, decimalPrice);
    }

    [Given(@"stub pricing client has ceiling price ""(.*)"" for SKU ""(.*)""")]
    public void GivenStubPricingClientHasCeilingPrice(string price, string sku)
    {
        var decimalPrice = decimal.Parse(price.Replace("$", ""));
        _fixture.StubPricingClient.SetCeilingPrice(sku, decimalPrice);
    }

    [When(@"I navigate to the price edit page for SKU ""(.*)""")]
    public async Task WhenINavigateToPriceEditPage(string sku)
    {
        _priceEditPage = new PriceEditPage(_page);
        await _priceEditPage.NavigateToAsync(sku);
    }

    [When(@"I set the price to ""(.*)""")]
    public async Task WhenISetThePrice(string price)
    {
        await _priceEditPage.SetPriceAsync(price);
    }

    [When(@"I submit the price change")]
    public async Task WhenISubmitThePriceChange()
    {
        await _priceEditPage.SubmitPriceAsync();
    }

    [Then(@"I should see the current price ""(.*)""")]
    public async Task ThenIShouldSeeTheCurrentPrice(string expectedPrice)
    {
        var actualPrice = await _priceEditPage.GetCurrentPriceAsync();
        actualPrice.Should().Contain(expectedPrice);
    }

    [Then(@"the current price should be ""(.*)""")]
    public async Task ThenTheCurrentPriceShouldBe(string expectedPrice)
    {
        await _page.WaitForTimeoutAsync(500); // Allow UI update
        var actualPrice = await _priceEditPage.GetCurrentPriceAsync();
        actualPrice.Should().Contain(expectedPrice);
    }

    [Then(@"the submit button should be disabled")]
    public async Task ThenTheSubmitButtonShouldBeDisabled()
    {
        var isDisabled = await _priceEditPage.IsSubmitButtonDisabledAsync();
        isDisabled.Should().BeTrue();
    }

    [Then(@"I should see the error message ""(.*)""")]
    public async Task ThenIShouldSeeTheErrorMessage(string expectedMessage)
    {
        var actualMessage = await _priceEditPage.GetErrorMessageAsync();
        actualMessage.Should().Contain(expectedMessage);
    }

    [Then(@"I should see the price edit form")]
    public async Task ThenIShouldSeeThePriceEditForm()
    {
        var isVisible = await _priceEditPage.IsPriceEditFormVisibleAsync();
        isVisible.Should().BeTrue();
    }
}
```

---

### Phase 2: Warehouse Admin E2E Tests (~90 min)

**2.1 Create WarehouseAdmin.feature** (`tests/Backoffice/Backoffice.E2ETests/Features/WarehouseAdmin.feature`):

```gherkin
Feature: Warehouse Admin
  As a Warehouse Clerk
  I want to manage inventory levels
  So that I can track stock accurately

  Background:
    Given the Backoffice system is running
    And stub catalog client has product "DEMO-001" with name "Cat Food Premium"
    And stub inventory client has SKU "DEMO-001" with available 50, reserved 10, total 60

  Scenario: Warehouse Clerk can browse inventory list
    Given admin user exists with email "warehouse@example.com" and role "WarehouseClerk"
    When I log in with email "warehouse@example.com" and password "password123"
    And I navigate to the inventory list page
    Then I should see inventory items
    And inventory item "DEMO-001" should have status "In Stock"

  Scenario: Filter inventory by SKU
    Given admin user exists with email "warehouse@example.com" and role "WarehouseClerk"
    When I log in with email "warehouse@example.com" and password "password123"
    And I navigate to the inventory list page
    And I search for SKU "DEMO-001"
    Then I should see 1 inventory item

  Scenario: Warehouse Clerk can adjust inventory (positive)
    Given admin user exists with email "warehouse@example.com" and role "WarehouseClerk"
    When I log in with email "warehouse@example.com" and password "password123"
    And I navigate to the inventory edit page for SKU "DEMO-001"
    Then I should see available quantity of 50
    When I adjust inventory by 10 units with reason "Cycle Count" adjusted by "warehouse@example.com"
    And I submit the adjustment
    Then I should see the success message "Inventory adjusted successfully"
    And the available quantity should be 60

  Scenario: Warehouse Clerk can adjust inventory (negative)
    Given admin user exists with email "warehouse@example.com" and role "WarehouseClerk"
    When I log in with email "warehouse@example.com" and password "password123"
    And I navigate to the inventory edit page for SKU "DEMO-001"
    And I adjust inventory by -5 units with reason "Damage" adjusted by "warehouse@example.com"
    And I submit the adjustment
    Then I should see the success message "Inventory adjusted successfully"
    And the available quantity should be 45

  Scenario: Warehouse Clerk can receive inbound stock
    Given admin user exists with email "warehouse@example.com" and role "WarehouseClerk"
    When I log in with email "warehouse@example.com" and password "password123"
    And I navigate to the inventory edit page for SKU "DEMO-001"
    When I receive 20 units from source "Supplier ABC"
    And I submit the stock receipt
    Then I should see the success message "Stock received successfully"
    And the available quantity should be 70

  Scenario: Adjustment quantity cannot be zero
    Given admin user exists with email "warehouse@example.com" and role "WarehouseClerk"
    When I log in with email "warehouse@example.com" and password "password123"
    And I navigate to the inventory edit page for SKU "DEMO-001"
    And I adjust inventory by 0 units with reason "Cycle Count" adjusted by "warehouse@example.com"
    Then the adjust inventory button should be disabled

  Scenario: Adjustment reason is required
    Given admin user exists with email "warehouse@example.com" and role "WarehouseClerk"
    When I log in with email "warehouse@example.com" and password "password123"
    And I navigate to the inventory edit page for SKU "DEMO-001"
    And I adjust inventory by 10 units with reason "" adjusted by "warehouse@example.com"
    Then the adjust inventory button should be disabled

  Scenario: Receive quantity must be positive
    Given admin user exists with email "warehouse@example.com" and role "WarehouseClerk"
    When I log in with email "warehouse@example.com" and password "password123"
    And I navigate to the inventory edit page for SKU "DEMO-001"
    And I receive 0 units from source "Supplier ABC"
    Then the receive stock button should be disabled

  Scenario: Session expired redirects to login
    Given admin user exists with email "warehouse@example.com" and role "WarehouseClerk"
    When I log in with email "warehouse@example.com" and password "password123"
    And I navigate to the inventory edit page for SKU "DEMO-001"
    And my session expires
    And I adjust inventory by 10 units with reason "Cycle Count" adjusted by "warehouse@example.com"
    And I submit the adjustment
    Then I should be redirected to the login page

  Scenario: SystemAdmin can manage inventory
    Given admin user exists with email "admin@example.com" and role "SystemAdmin"
    When I log in with email "admin@example.com" and password "password123"
    And I navigate to the inventory list page
    Then I should see inventory items
```

**2.2 Create InventoryListPage.cs** (`tests/Backoffice/Backoffice.E2ETests/PageObjects/InventoryListPage.cs`):

```csharp
namespace Backoffice.E2ETests.PageObjects;

public sealed class InventoryListPage
{
    private readonly IPage _page;

    public InventoryListPage(IPage page) => _page = page;

    public async Task NavigateToAsync()
    {
        await _page.GotoAsync("/inventory");
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    public async Task SearchBySkuAsync(string sku)
    {
        var input = _page.GetByTestId("search-sku-input");
        await input.FillAsync(sku);
        await _page.WaitForTimeoutAsync(300); // Client-side filter
    }

    public async Task<int> GetInventoryItemCountAsync()
    {
        var rows = _page.GetByTestId("inventory-row");
        return await rows.CountAsync();
    }

    public async Task<string> GetStockStatusAsync(string sku)
    {
        var row = _page.GetByTestId($"inventory-row-{sku}");
        var chip = row.GetByTestId("stock-status-chip");
        return await chip.InnerTextAsync();
    }

    public async Task ClickRowAsync(string sku)
    {
        var row = _page.GetByTestId($"inventory-row-{sku}");
        await row.ClickAsync();
    }

    public async Task<bool> IsInventoryTableVisibleAsync()
    {
        var table = _page.GetByTestId("inventory-table");
        return await table.IsVisibleAsync();
    }
}
```

**2.3 Create InventoryEditPage.cs** (`tests/Backoffice/Backoffice.E2ETests/PageObjects/InventoryEditPage.cs`):

```csharp
namespace Backoffice.E2ETests.PageObjects;

public sealed class InventoryEditPage
{
    private readonly IPage _page;

    public InventoryEditPage(IPage page) => _page = page;

    public async Task NavigateToAsync(string sku)
    {
        await _page.GotoAsync($"/inventory/{Uri.EscapeDataString(sku)}/edit");
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    public async Task<int> GetAvailableQuantityAsync()
    {
        var element = _page.GetByTestId("available-quantity");
        var text = await element.InnerTextAsync();
        return int.Parse(text);
    }

    public async Task<int> GetReservedQuantityAsync()
    {
        var element = _page.GetByTestId("reserved-quantity");
        var text = await element.InnerTextAsync();
        return int.Parse(text);
    }

    public async Task<int> GetTotalQuantityAsync()
    {
        var element = _page.GetByTestId("total-quantity");
        var text = await element.InnerTextAsync();
        return int.Parse(text);
    }

    public async Task AdjustInventoryAsync(int quantity, string reason, string adjustedBy)
    {
        var quantityInput = _page.GetByTestId("adjust-quantity-input");
        await quantityInput.FillAsync(quantity.ToString());

        var reasonSelect = _page.GetByTestId("adjust-reason-select");
        await reasonSelect.SelectOptionAsync(new[] { reason });

        var adjustedByInput = _page.GetByTestId("adjusted-by-input");
        await adjustedByInput.FillAsync(adjustedBy);
    }

    public async Task SubmitAdjustmentAsync()
    {
        var button = _page.GetByTestId("adjust-inventory-button");
        await button.ClickAsync();
        await _page.WaitForTimeoutAsync(500);
    }

    public async Task ReceiveStockAsync(int quantity, string source)
    {
        var quantityInput = _page.GetByTestId("receive-quantity-input");
        await quantityInput.FillAsync(quantity.ToString());

        var sourceInput = _page.GetByTestId("receive-source-input");
        await sourceInput.FillAsync(source);
    }

    public async Task SubmitStockReceiptAsync()
    {
        var button = _page.GetByTestId("receive-stock-button");
        await button.ClickAsync();
        await _page.WaitForTimeoutAsync(500);
    }

    public async Task<string> GetSuccessMessageAsync()
    {
        var element = _page.GetByTestId("success-message");
        return await element.InnerTextAsync();
    }

    public async Task<string> GetErrorMessageAsync()
    {
        var element = _page.GetByTestId("error-message");
        return await element.InnerTextAsync();
    }

    public async Task<bool> IsAdjustButtonDisabledAsync()
    {
        var button = _page.GetByTestId("adjust-inventory-button");
        return await button.IsDisabledAsync();
    }

    public async Task<bool> IsReceiveButtonDisabledAsync()
    {
        var button = _page.GetByTestId("receive-stock-button");
        return await button.IsDisabledAsync();
    }
}
```

**2.4 Create WarehouseAdminSteps.cs** (`tests/Backoffice/Backoffice.E2ETests/StepDefinitions/WarehouseAdminSteps.cs`):

```csharp
namespace Backoffice.E2ETests.StepDefinitions;

[Binding]
public sealed class WarehouseAdminSteps
{
    private readonly E2ETestFixture _fixture;
    private readonly IPage _page;
    private InventoryListPage _inventoryListPage = null!;
    private InventoryEditPage _inventoryEditPage = null!;

    public WarehouseAdminSteps(E2ETestFixture fixture, IPage page)
    {
        _fixture = fixture;
        _page = page;
    }

    [Given(@"stub inventory client has SKU ""(.*)"" with available (\d+), reserved (\d+), total (\d+)")]
    public void GivenStubInventoryClientHasStockLevels(string sku, int available, int reserved, int total)
    {
        _fixture.StubInventoryClient.SetStockLevels(sku, available, reserved, total);
    }

    [When(@"I navigate to the inventory list page")]
    public async Task WhenINavigateToInventoryListPage()
    {
        _inventoryListPage = new InventoryListPage(_page);
        await _inventoryListPage.NavigateToAsync();
    }

    [When(@"I search for SKU ""(.*)""")]
    public async Task WhenISearchForSku(string sku)
    {
        await _inventoryListPage.SearchBySkuAsync(sku);
    }

    [When(@"I navigate to the inventory edit page for SKU ""(.*)""")]
    public async Task WhenINavigateToInventoryEditPage(string sku)
    {
        _inventoryEditPage = new InventoryEditPage(_page);
        await _inventoryEditPage.NavigateToAsync(sku);
    }

    [When(@"I adjust inventory by (-?\d+) units with reason ""(.*)"" adjusted by ""(.*)""")]
    public async Task WhenIAdjustInventory(int quantity, string reason, string adjustedBy)
    {
        await _inventoryEditPage.AdjustInventoryAsync(quantity, reason, adjustedBy);
    }

    [When(@"I submit the adjustment")]
    public async Task WhenISubmitTheAdjustment()
    {
        await _inventoryEditPage.SubmitAdjustmentAsync();
    }

    [When(@"I receive (\d+) units from source ""(.*)""")]
    public async Task WhenIReceiveStock(int quantity, string source)
    {
        await _inventoryEditPage.ReceiveStockAsync(quantity, source);
    }

    [When(@"I submit the stock receipt")]
    public async Task WhenISubmitTheStockReceipt()
    {
        await _inventoryEditPage.SubmitStockReceiptAsync();
    }

    [Then(@"I should see inventory items")]
    public async Task ThenIShouldSeeInventoryItems()
    {
        var isVisible = await _inventoryListPage.IsInventoryTableVisibleAsync();
        isVisible.Should().BeTrue();
    }

    [Then(@"I should see (\d+) inventory item")]
    public async Task ThenIShouldSeeInventoryItemCount(int expectedCount)
    {
        var actualCount = await _inventoryListPage.GetInventoryItemCountAsync();
        actualCount.Should().Be(expectedCount);
    }

    [Then(@"inventory item ""(.*)"" should have status ""(.*)""")]
    public async Task ThenInventoryItemShouldHaveStatus(string sku, string expectedStatus)
    {
        var actualStatus = await _inventoryListPage.GetStockStatusAsync(sku);
        actualStatus.Should().Contain(expectedStatus);
    }

    [Then(@"I should see available quantity of (\d+)")]
    public async Task ThenIShouldSeeAvailableQuantity(int expectedQuantity)
    {
        var actualQuantity = await _inventoryEditPage.GetAvailableQuantityAsync();
        actualQuantity.Should().Be(expectedQuantity);
    }

    [Then(@"the available quantity should be (\d+)")]
    public async Task ThenTheAvailableQuantityShouldBe(int expectedQuantity)
    {
        await _page.WaitForTimeoutAsync(500); // Allow UI update
        var actualQuantity = await _inventoryEditPage.GetAvailableQuantityAsync();
        actualQuantity.Should().Be(expectedQuantity);
    }

    [Then(@"the adjust inventory button should be disabled")]
    public async Task ThenTheAdjustButtonShouldBeDisabled()
    {
        var isDisabled = await _inventoryEditPage.IsAdjustButtonDisabledAsync();
        isDisabled.Should().BeTrue();
    }

    [Then(@"the receive stock button should be disabled")]
    public async Task ThenTheReceiveButtonShouldBeDisabled()
    {
        var isDisabled = await _inventoryEditPage.IsReceiveButtonDisabledAsync();
        isDisabled.Should().BeTrue();
    }
}
```

---

### Phase 3: Update Stub Clients (~20 min)

**3.1 Extend StubPricingClient** (`tests/Backoffice/Backoffice.E2ETests/Stubs/StubPricingClient.cs`):

```csharp
// Add support for floor/ceiling prices
private readonly Dictionary<string, decimal> _floorPrices = new();
private readonly Dictionary<string, decimal> _ceilingPrices = new();

public void SetFloorPrice(string sku, decimal floorPrice)
{
    _floorPrices[sku] = floorPrice;
}

public void SetCeilingPrice(string sku, decimal ceilingPrice)
{
    _ceilingPrices[sku] = ceilingPrice;
}

// Update SetBasePriceAsync to enforce constraints
public async Task<SetBasePriceResultDto?> SetBasePriceAsync(string sku, decimal newBasePrice, string changedBy, CancellationToken ct = default)
{
    if (SimulateSessionExpired)
        return null;

    if (_floorPrices.TryGetValue(sku, out var floor) && newBasePrice < floor)
        throw new HttpRequestException($"Price cannot be below floor price of ${floor:F2}");

    if (_ceilingPrices.TryGetValue(sku, out var ceiling) && newBasePrice > ceiling)
        throw new HttpRequestException($"Price cannot exceed ceiling price of ${ceiling:F2}");

    _currentPrices[sku] = newBasePrice;
    return new SetBasePriceResultDto(sku, newBasePrice, changedBy, DateTimeOffset.UtcNow);
}
```

**3.2 Extend StubInventoryClient** (`tests/Backoffice/Backoffice.E2ETests/Stubs/StubInventoryClient.cs`):

```csharp
// Already has in-memory stock levels from Session 4
// Just verify methods exist: SetStockLevels, AdjustInventoryAsync, ReceiveInboundStockAsync
// No changes needed if Session 4 implementation was complete
```

---

### Phase 4: Add data-testid Attributes to UI Pages (~30 min)

**4.1 Update PriceEdit.razor** (`src/Backoffice/Backoffice.Web/Pages/Products/PriceEdit.razor`):

Add `data-testid` attributes to:
- Current price display: `data-testid="current-price"`
- Price input field: `data-testid="price-input"`
- Submit button: `data-testid="submit-price-button"`
- Success message: `data-testid="success-message"`
- Error message: `data-testid="error-message"`
- Form container: `data-testid="price-edit-form"`

**4.2 Update InventoryList.razor** (`src/Backoffice/Backoffice.Web/Pages/Inventory/InventoryList.razor`):

Add `data-testid` attributes to:
- Search input: `data-testid="search-sku-input"`
- Table container: `data-testid="inventory-table"`
- Table rows: `data-testid="inventory-row-{sku}"` (dynamic)
- Status chips: `data-testid="stock-status-chip"`

**4.3 Update InventoryEdit.razor** (`src/Backoffice/Backoffice.Web/Pages/Inventory/InventoryEdit.razor`):

Add `data-testid` attributes to:
- Available KPI: `data-testid="available-quantity"`
- Reserved KPI: `data-testid="reserved-quantity"`
- Total KPI: `data-testid="total-quantity"`
- Adjust quantity input: `data-testid="adjust-quantity-input"`
- Adjust reason select: `data-testid="adjust-reason-select"`
- Adjusted by input: `data-testid="adjusted-by-input"`
- Adjust button: `data-testid="adjust-inventory-button"`
- Receive quantity input: `data-testid="receive-quantity-input"`
- Receive source input: `data-testid="receive-source-input"`
- Receive button: `data-testid="receive-stock-button"`
- Success message: `data-testid="success-message"`
- Error message: `data-testid="error-message"`

---

### Phase 5: Build & Run Tests (~20 min)

**5.1 Build solution:**
```bash
dotnet build
```

**5.2 Run E2E tests:**
```bash
cd tests/Backoffice/Backoffice.E2ETests
dotnet test --filter "Category=PricingAdmin|Category=WarehouseAdmin"
```

**5.3 Review test results:**
- Target: 80%+ pass rate (14+ scenarios passing out of 17 total)
- Acceptable: Some timing issues or flakiness (will be addressed in Session 9)
- Blocker: Compilation errors or fixture startup failures

**5.4 Generate Playwright traces (if failures occur):**
```bash
playwright show-trace playwright-traces/<test-name>.zip
```

---

## Success Criteria

- ✅ PricingAdmin.feature created with 6 scenarios
- ✅ WarehouseAdmin.feature created with 10 scenarios
- ✅ PriceEditPage, InventoryListPage, InventoryEditPage Page Objects created
- ✅ PricingAdminSteps, WarehouseAdminSteps step definitions created
- ✅ Stub clients extended (floor/ceiling price constraints)
- ✅ UI pages updated with data-testid attributes
- ✅ Build succeeds with 0 errors
- ✅ At least 80% of scenarios pass (14+ out of 17)

---

## Risks & Mitigations

### R1: UI pages missing data-testid attributes

**Risk:** PriceEdit.razor and Inventory pages may not have data-testid attributes (Session 3-4 focused on functionality).

**Mitigation:**
- Phase 4 adds all required attributes
- Follow existing patterns from ProductEdit.razor (Session 1)

**Status:** Planned for Phase 4.

### R2: Stub client floor/ceiling enforcement

**Risk:** StubPricingClient may not enforce floor/ceiling constraints.

**Mitigation:**
- Phase 3 adds constraint validation to stub
- Throws HttpRequestException to simulate backend error

**Status:** Planned for Phase 3.

### R3: E2E test flakiness

**Risk:** New tests may have timing issues (Blazor WASM hydration, SignalR delays).

**Mitigation:**
- Use existing timeout patterns from M32.1 tests
- Add WaitForTimeoutAsync(500) after state changes
- Playwright tracing enabled for debugging

**Status:** Accepted — will refine in Session 9.

---

## Deferred Work

### Deferred to Session 6+

1. **User Management write UI** (Session 6)
2. **CSV/Excel exports** (Session 7)
3. **Bulk operations pattern** (Session 8)
4. **Comprehensive E2E test coverage** (Session 9 — stabilize all tests, add smoke tests)
5. **Documentation and retrospective** (Session 10)

---

## Next Steps After Session 5

### Immediate (Session 6)

- User Management write UI (CreateAdminUser, ChangeRole, DeactivateUser pages)

### Future (Sessions 7-10)

- CSV/Excel exports (Session 7)
- Bulk operations pattern (Session 8)
- Comprehensive E2E stabilization (Session 9)
- Documentation and M32.3 retrospective (Session 10)

---

## References

- **M32.3 Session 1 Retrospective:** `docs/planning/milestones/m32-3-session-1-retrospective.md`
- **M32.3 Session 2 Retrospective:** `docs/planning/milestones/m32-3-session-2-retrospective.md`
- **M32.3 Session 4 Retrospective:** `docs/planning/milestones/m32-3-session-4-retrospective.md`
- **M32.1 Session 9:** E2E test infrastructure created
- **M32.2 Session 2:** SessionExpiredService pattern established
- **Product Admin E2E Tests:** `tests/Backoffice/Backoffice.E2ETests/Features/ProductAdmin.feature`
- **Skills:**
  - `docs/skills/e2e-playwright-testing.md` — E2E testing patterns
  - `docs/skills/reqnroll-bdd-testing.md` — BDD with Gherkin
  - `docs/skills/blazor-wasm-jwt.md` — WASM client patterns

---

**Plan Status:** ✅ Ready for execution
**Estimated Duration:** ~3 hours
**Complexity:** Medium-High (E2E tests require careful data-testid alignment and stub coordination)
