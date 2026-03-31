using Backoffice.E2ETests.Pages;
using Microsoft.Playwright;
using Reqnroll;
using Shouldly;

namespace Backoffice.E2ETests.StepDefinitions;

/// <summary>
/// Step definitions for Marketplace Administration E2E scenarios.
/// Covers MarketplacesAdmin.feature — marketplace list page (3 scenarios)
/// and category mappings list page (3 scenarios).
///
/// M36.1 Session 9: E2E coverage for marketplace admin pages.
/// Uses StubMarketplacesApiHost for test data seeding — no real Marketplaces.Api required.
/// All selectors target data-testid attributes confirmed in Session 9 audit.
/// </summary>
[Binding]
public sealed class MarketplacesAdminSteps
{
    private readonly ScenarioContext _scenarioContext;

    public MarketplacesAdminSteps(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
    }

    private E2ETestFixture Fixture => _scenarioContext.Get<E2ETestFixture>(ScenarioContextKeys.Fixture);
    private IPage Page => _scenarioContext.Get<IPage>(ScenarioContextKeys.Page);

    // ─── Given Steps ────────────────────────────────────────────────────────

    [Given(@"test marketplace data exists in the Marketplaces service")]
    public void GivenTestMarketplaceDataExistsInTheMarketplacesService()
    {
        // Seed 3 canonical marketplaces (mirrors MarketplacesSeedData)
        Fixture.StubMarketplacesApi.SeedMarketplace("AMAZON_US", "Amazon US", isActive: true);
        Fixture.StubMarketplacesApi.SeedMarketplace("EBAY_US", "eBay US", isActive: true);
        Fixture.StubMarketplacesApi.SeedMarketplace("WALMART_US", "Walmart US", isActive: true);

        // Seed 18 category mappings (6 categories × 3 channels)
        var categories = new[]
        {
            ("Dogs", "DOGS"), ("Cats", "CATS"), ("Birds", "BIRDS"),
            ("Reptiles", "REPT"), ("Fish & Aquatics", "FISH"), ("Small Animals", "SMALL")
        };

        foreach (var (name, code) in categories)
        {
            Fixture.StubMarketplacesApi.SeedCategoryMapping("AMAZON_US", name, $"AMZN-PET-{code}-001");
            Fixture.StubMarketplacesApi.SeedCategoryMapping("WALMART_US", name, $"WMT-PET-{code}-001");
            Fixture.StubMarketplacesApi.SeedCategoryMapping("EBAY_US", name, $"EBAY-PET-{code}-001");
        }
    }

    [Given(@"I am on the category mappings page")]
    public async Task GivenIAmOnTheCategoryMappingsPage()
    {
        var page = new CategoryMappingsListPage(Page, Fixture.WasmBaseUrl);
        await page.NavigateAsync();
    }

    // ─── When Steps ─────────────────────────────────────────────────────────

    [When(@"I navigate to the marketplaces list page")]
    public async Task WhenINavigateToTheMarketplacesListPage()
    {
        var page = new MarketplacesListPage(Page, Fixture.WasmBaseUrl);
        await page.NavigateAsync();
    }

    [When(@"I navigate to the category mappings page")]
    public async Task WhenINavigateToTheCategoryMappingsPage()
    {
        var page = new CategoryMappingsListPage(Page, Fixture.WasmBaseUrl);
        await page.NavigateAsync();
    }

    [When(@"I navigate directly to ""(.*)""")]
    public async Task WhenINavigateDirectlyTo(string path)
    {
        await Page.GotoAsync($"{Fixture.WasmBaseUrl}{path}", new PageGotoOptions { WaitUntil = WaitUntilState.Commit });
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Wait for Blazor WASM to hydrate and process auth redirect
        await Page.WaitForSelectorAsync(".mud-dialog-provider",
            new() { State = WaitForSelectorState.Attached, Timeout = 60_000 });
    }

    [When(@"I filter category mappings by channel ""(.*)""")]
    public async Task WhenIFilterCategoryMappingsByChannel(string channelDisplayName)
    {
        var page = new CategoryMappingsListPage(Page, Fixture.WasmBaseUrl);
        await page.FilterByChannelAsync(channelDisplayName);
    }

    // ─── Then Steps: Marketplace List ───────────────────────────────────────

    [Then(@"I should see the marketplaces table")]
    public async Task ThenIShouldSeeTheMarketplacesTable()
    {
        var page = new MarketplacesListPage(Page, Fixture.WasmBaseUrl);
        var isVisible = await page.IsTableVisibleAsync();
        isVisible.ShouldBeTrue("Expected marketplaces-table to be visible");
    }

    [Then(@"I should see (\d+) marketplace rows")]
    public async Task ThenIShouldSeeMarketplaceRows(int expectedCount)
    {
        var page = new MarketplacesListPage(Page, Fixture.WasmBaseUrl);
        var rowCount = await page.GetRowCountAsync();
        rowCount.ShouldBe(expectedCount, $"Expected {expectedCount} marketplace rows but found {rowCount}");
    }

    [Then(@"I should see marketplace row ""(.*)"" with display name ""(.*)""")]
    public async Task ThenIShouldSeeMarketplaceRowWithDisplayName(string channelCode, string expectedDisplayName)
    {
        var page = new MarketplacesListPage(Page, Fixture.WasmBaseUrl);
        var isVisible = await page.IsRowVisibleAsync(channelCode);
        isVisible.ShouldBeTrue($"Expected marketplace row for {channelCode} to be visible");

        var displayName = await page.GetDisplayNameAsync(channelCode);
        displayName.ShouldNotBeNull();
        displayName!.Trim().ShouldBe(expectedDisplayName,
            $"Expected display name '{expectedDisplayName}' for {channelCode} but got '{displayName}'");
    }

    [Then(@"marketplace ""(.*)"" should show status ""(.*)""")]
    public async Task ThenMarketplaceShouldShowStatus(string channelCode, string expectedStatus)
    {
        var page = new MarketplacesListPage(Page, Fixture.WasmBaseUrl);
        var statusText = await page.GetStatusChipTextAsync(channelCode);
        statusText.ShouldNotBeNull($"Expected status chip for {channelCode} to be visible");
        statusText!.Trim().ShouldBe(expectedStatus,
            $"Expected status '{expectedStatus}' for {channelCode} but got '{statusText}'");
    }

    [Then(@"I should be on the login page")]
    public async Task ThenIShouldBeOnTheLoginPage()
    {
        // Wait for Blazor WASM to process auth redirect
        await Page.WaitForURLAsync(url => url.Contains("/login"),
            new() { Timeout = 15_000, WaitUntil = WaitUntilState.Commit });

        var loginPage = new LoginPage(Page, Fixture.WasmBaseUrl);
        var isOnLogin = await loginPage.IsOnLoginPageAsync();
        isOnLogin.ShouldBeTrue("Expected to be redirected to the login page");
    }

    // ─── Then Steps: Category Mappings ──────────────────────────────────────

    [Then(@"I should see the category mappings table")]
    public async Task ThenIShouldSeeTheCategoryMappingsTable()
    {
        var page = new CategoryMappingsListPage(Page, Fixture.WasmBaseUrl);
        var isVisible = await page.IsTableVisibleAsync();
        isVisible.ShouldBeTrue("Expected category-mappings-table to be visible");
    }

    [Then(@"I should see (\d+) category mapping rows")]
    public async Task ThenIShouldSeeCategoryMappingRows(int expectedCount)
    {
        var page = new CategoryMappingsListPage(Page, Fixture.WasmBaseUrl);
        var rowCount = await page.GetRowCountAsync();
        rowCount.ShouldBe(expectedCount, $"Expected {expectedCount} category mapping rows but found {rowCount}");
    }

    [Then(@"I should see exactly (\d+) category mapping rows")]
    public async Task ThenIShouldSeeExactlyCategoryMappingRows(int expectedCount)
    {
        // Wait for filter to take effect
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var page = new CategoryMappingsListPage(Page, Fixture.WasmBaseUrl);
        var rowCount = await page.GetRowCountAsync();
        rowCount.ShouldBe(expectedCount, $"Expected exactly {expectedCount} category mapping rows after filter but found {rowCount}");
    }

    [Then(@"I should see the breadcrumb trail")]
    public async Task ThenIShouldSeeTheBreadcrumbTrail()
    {
        var page = new CategoryMappingsListPage(Page, Fixture.WasmBaseUrl);
        var isVisible = await page.IsBreadcrumbVisibleAsync();
        isVisible.ShouldBeTrue("Expected breadcrumbs to be visible");
    }

    [Then(@"the breadcrumb trail should contain ""(.*)""")]
    public async Task ThenTheBreadcrumbTrailShouldContain(string expectedText)
    {
        var page = new CategoryMappingsListPage(Page, Fixture.WasmBaseUrl);
        var breadcrumbText = await page.GetBreadcrumbTextAsync();
        breadcrumbText.ShouldNotBeNull("Expected breadcrumb text to be non-null");
        breadcrumbText!.ShouldContain(expectedText,
            $"Expected breadcrumb trail to contain '{expectedText}' but got '{breadcrumbText}'");
    }
}
