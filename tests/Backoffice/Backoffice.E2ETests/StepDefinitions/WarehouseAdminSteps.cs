using Backoffice.E2ETests.Pages;
using Microsoft.Playwright;
using Reqnroll;
using Shouldly;

namespace Backoffice.E2ETests.StepDefinitions;

[Binding]
public sealed class WarehouseAdminSteps
{
    private readonly ScenarioContext _scenarioContext;

    public WarehouseAdminSteps(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
    }

    private E2ETestFixture Fixture => _scenarioContext.Get<E2ETestFixture>(ScenarioContextKeys.Fixture);
    private IPage Page => _scenarioContext.Get<IPage>(ScenarioContextKeys.Page);

    // --- Given Steps: Stub Configuration ---

    [Given(@"stub inventory has SKU ""(.*)"" with (\d+) available and (\d+) reserved")]
    public void GivenStubInventoryHasSkuWithAvailableAndReserved(string sku, int available, int reserved)
    {
        Fixture.StubInventoryClient.SetStockLevel(sku, available, reserved);
    }

    // --- When Steps: Navigation ---

    [When(@"I navigate to the inventory list")]
    public async Task WhenINavigateToTheInventoryList()
    {
        var inventoryListPage = new InventoryListPage(Page, Fixture.WasmBaseUrl);
        await inventoryListPage.NavigateAsync();
    }

    [When(@"I navigate to the inventory edit page for SKU ""(.*)""")]
    public async Task WhenINavigateToTheInventoryEditPageForSku(string sku)
    {
        var inventoryEditPage = new InventoryEditPage(Page, Fixture.WasmBaseUrl);
        await inventoryEditPage.NavigateAsync(sku);
    }

    [When(@"I search inventory for ""(.*)""")]
    public async Task WhenISearchInventoryFor(string searchTerm)
    {
        var inventoryListPage = new InventoryListPage(Page, Fixture.WasmBaseUrl);
        await inventoryListPage.SearchForSkuAsync(searchTerm);
    }

    [When(@"I click on SKU ""(.*)"" in the inventory list")]
    public async Task WhenIClickOnSkuInTheInventoryList(string sku)
    {
        var inventoryListPage = new InventoryListPage(Page, Fixture.WasmBaseUrl);
        await inventoryListPage.ClickSkuAsync(sku);
    }

    // --- When Steps: Adjust Inventory ---

    [When(@"I set the adjustment quantity to ""(.*)""")]
    public async Task WhenISetTheAdjustmentQuantityTo(string quantity)
    {
        var inventoryEditPage = new InventoryEditPage(Page, Fixture.WasmBaseUrl);
        await inventoryEditPage.SetAdjustmentQuantityAsync(quantity);
    }

    [When(@"I select the adjustment reason ""(.*)""")]
    public async Task WhenISelectTheAdjustmentReason(string reason)
    {
        var inventoryEditPage = new InventoryEditPage(Page, Fixture.WasmBaseUrl);
        await inventoryEditPage.SelectAdjustmentReasonAsync(reason);
    }

    [When(@"I submit the inventory adjustment")]
    public async Task WhenISubmitTheInventoryAdjustment()
    {
        var inventoryEditPage = new InventoryEditPage(Page, Fixture.WasmBaseUrl);
        await inventoryEditPage.SubmitAdjustmentAsync();
    }

    // --- When Steps: Receive Stock ---

    [When(@"I set the receive quantity to ""(.*)""")]
    public async Task WhenISetTheReceiveQuantityTo(string quantity)
    {
        var inventoryEditPage = new InventoryEditPage(Page, Fixture.WasmBaseUrl);
        await inventoryEditPage.SetReceiveQuantityAsync(quantity);
    }

    [When(@"I set the receive source to ""(.*)""")]
    public async Task WhenISetTheReceiveSourceTo(string source)
    {
        var inventoryEditPage = new InventoryEditPage(Page, Fixture.WasmBaseUrl);
        await inventoryEditPage.SetReceiveSourceAsync(source);
    }

    [When(@"I submit the stock receipt")]
    public async Task WhenISubmitTheStockReceipt()
    {
        var inventoryEditPage = new InventoryEditPage(Page, Fixture.WasmBaseUrl);
        await inventoryEditPage.SubmitReceiveAsync();
    }

    // --- Then Steps: Inventory List ---

    [Then(@"I should see the inventory table")]
    public async Task ThenIShouldSeeTheInventoryTable()
    {
        var inventoryListPage = new InventoryListPage(Page, Fixture.WasmBaseUrl);
        var isVisible = await inventoryListPage.IsInventoryTableVisibleAsync();
        isVisible.ShouldBeTrue();
    }

    [Then(@"I should see SKU ""(.*)"" in the inventory list")]
    public async Task ThenIShouldSeeSkuInTheInventoryList(string sku)
    {
        var inventoryListPage = new InventoryListPage(Page, Fixture.WasmBaseUrl);
        var isVisible = await inventoryListPage.IsSkuVisibleAsync(sku);
        isVisible.ShouldBeTrue();
    }

    [Then(@"I should not see SKU ""(.*)"" in the inventory list")]
    public async Task ThenIShouldNotSeeSkuInTheInventoryList(string sku)
    {
        var inventoryListPage = new InventoryListPage(Page, Fixture.WasmBaseUrl);
        var isVisible = await inventoryListPage.IsSkuVisibleAsync(sku);
        isVisible.ShouldBeFalse();
    }

    // --- Then Steps: Inventory Edit Page ---

    [Then(@"I should be on the inventory edit page for ""(.*)""")]
    public async Task ThenIShouldBeOnTheInventoryEditPageFor(string sku)
    {
        var inventoryEditPage = new InventoryEditPage(Page, Fixture.WasmBaseUrl);
        var isOnPage = await inventoryEditPage.IsOnPageForSkuAsync(sku);
        isOnPage.ShouldBeTrue();
    }

    [Then(@"I should see the available quantity is ""(.*)""")]
    public async Task ThenIShouldSeeTheAvailableQuantityIs(string expectedQuantity)
    {
        var inventoryEditPage = new InventoryEditPage(Page, Fixture.WasmBaseUrl);
        var actual = await inventoryEditPage.GetAvailableQuantityAsync();
        actual.ShouldBe(expectedQuantity);
    }

    [Then(@"I should see the reserved quantity is ""(.*)""")]
    public async Task ThenIShouldSeeTheReservedQuantityIs(string expectedQuantity)
    {
        var inventoryEditPage = new InventoryEditPage(Page, Fixture.WasmBaseUrl);
        var actual = await inventoryEditPage.GetReservedQuantityAsync();
        actual.ShouldBe(expectedQuantity);
    }

    [Then(@"I should see the total quantity is ""(.*)""")]
    public async Task ThenIShouldSeeTheTotalQuantityIs(string expectedQuantity)
    {
        var inventoryEditPage = new InventoryEditPage(Page, Fixture.WasmBaseUrl);
        var actual = await inventoryEditPage.GetTotalQuantityAsync();
        actual.ShouldBe(expectedQuantity);
    }

    [Then(@"the available quantity should be updated to ""(.*)""")]
    public async Task ThenTheAvailableQuantityShouldBeUpdatedTo(string expectedQuantity)
    {
        // Allow UI update after submission
        await Page.WaitForTimeoutAsync(500);
        var inventoryEditPage = new InventoryEditPage(Page, Fixture.WasmBaseUrl);
        var actual = await inventoryEditPage.GetAvailableQuantityAsync();
        actual.ShouldBe(expectedQuantity);
    }

    // --- Then Steps: Form Visibility ---

    [Then(@"I should see the adjust inventory form")]
    public async Task ThenIShouldSeeTheAdjustInventoryForm()
    {
        var inventoryEditPage = new InventoryEditPage(Page, Fixture.WasmBaseUrl);
        var isVisible = await inventoryEditPage.IsAdjustFormVisibleAsync();
        isVisible.ShouldBeTrue();
    }

    [Then(@"I should see the receive stock form")]
    public async Task ThenIShouldSeeTheReceiveStockForm()
    {
        var inventoryEditPage = new InventoryEditPage(Page, Fixture.WasmBaseUrl);
        var isVisible = await inventoryEditPage.IsReceiveFormVisibleAsync();
        isVisible.ShouldBeTrue();
    }

    // --- Then Steps: Button State ---

    [Then(@"the adjust submit button should be disabled")]
    public async Task ThenTheAdjustSubmitButtonShouldBeDisabled()
    {
        var inventoryEditPage = new InventoryEditPage(Page, Fixture.WasmBaseUrl);
        var isDisabled = await inventoryEditPage.IsAdjustSubmitDisabledAsync();
        isDisabled.ShouldBeTrue();
    }

    [Then(@"the receive submit button should be disabled")]
    public async Task ThenTheReceiveSubmitButtonShouldBeDisabled()
    {
        var inventoryEditPage = new InventoryEditPage(Page, Fixture.WasmBaseUrl);
        var isDisabled = await inventoryEditPage.IsReceiveSubmitDisabledAsync();
        isDisabled.ShouldBeTrue();
    }

    // --- Then Steps: Feedback Messages ---

    [Then(@"I should see the inventory success message ""(.*)""")]
    public async Task ThenIShouldSeeTheInventorySuccessMessage(string expectedMessage)
    {
        // Wait for success message to appear
        await Page.WaitForTimeoutAsync(1000);
        var inventoryEditPage = new InventoryEditPage(Page, Fixture.WasmBaseUrl);
        var actualMessage = await inventoryEditPage.GetSuccessMessageAsync();
        actualMessage.ShouldContain(expectedMessage);
    }
}
