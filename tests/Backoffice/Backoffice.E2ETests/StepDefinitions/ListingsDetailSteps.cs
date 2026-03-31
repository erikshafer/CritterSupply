using Backoffice.E2ETests.Pages;

namespace Backoffice.E2ETests.StepDefinitions;

/// <summary>
/// Step definitions for Listing Detail E2E scenarios.
/// Covers ListingsDetail.feature (1 executable scenario) — detail page field assertions
/// and action button state verification.
///
/// M36.1 Session 5: Separated from ListingsAdminSteps for clarity.
/// Navigation steps (Given/When) live in ListingsAdminSteps; this file owns detail page Then assertions.
/// All selectors target data-testid attributes confirmed in Session 4 retrospective.
/// </summary>
[Binding]
public sealed class ListingsDetailSteps
{
    private readonly ScenarioContext _scenarioContext;

    public ListingsDetailSteps(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
    }

    private E2ETestFixture Fixture => _scenarioContext.Get<E2ETestFixture>(ScenarioContextKeys.Fixture);
    private IPage Page => _scenarioContext.Get<IPage>(ScenarioContextKeys.Page);

    // ─── Detail Page Field Assertions ───────────────────────────────────────

    [Then(@"I should see the listing SKU")]
    public async Task ThenIShouldSeeTheListingSku()
    {
        var listingDetailPage = new ListingDetailPage(Page, Fixture.WasmBaseUrl);
        var sku = await listingDetailPage.GetSkuAsync();
        sku.ShouldNotBeNullOrWhiteSpace("Expected listing SKU to be displayed");
    }

    [Then(@"I should see the listing channel")]
    public async Task ThenIShouldSeeTheListingChannel()
    {
        var listingDetailPage = new ListingDetailPage(Page, Fixture.WasmBaseUrl);
        var channel = await listingDetailPage.GetChannelAsync();
        channel.ShouldNotBeNullOrWhiteSpace("Expected listing channel to be displayed");
    }

    [Then(@"I should see the listing status badge")]
    public async Task ThenIShouldSeeTheListingStatusBadge()
    {
        var listingDetailPage = new ListingDetailPage(Page, Fixture.WasmBaseUrl);
        var status = await listingDetailPage.GetStatusAsync();
        status.ShouldNotBeNullOrWhiteSpace("Expected listing status badge to be displayed");
    }

    [Then(@"I should see the listing product name")]
    public async Task ThenIShouldSeeTheListingProductName()
    {
        var listingDetailPage = new ListingDetailPage(Page, Fixture.WasmBaseUrl);
        var productName = await listingDetailPage.GetProductNameAsync();
        productName.ShouldNotBeNullOrWhiteSpace("Expected listing product name to be displayed");
    }

    [Then(@"I should see the listing created at timestamp")]
    public async Task ThenIShouldSeeTheListingCreatedAtTimestamp()
    {
        var listingDetailPage = new ListingDetailPage(Page, Fixture.WasmBaseUrl);
        var createdAt = await listingDetailPage.GetCreatedAtAsync();
        createdAt.ShouldNotBeNullOrWhiteSpace("Expected listing created at timestamp to be displayed");
    }

    // ─── Action Button State Assertions ─────────────────────────────────────

    [Then(@"the approve button should be disabled")]
    public async Task ThenTheApproveButtonShouldBeDisabled()
    {
        var listingDetailPage = new ListingDetailPage(Page, Fixture.WasmBaseUrl);
        var isDisabled = await listingDetailPage.IsApproveButtonDisabledAsync();
        isDisabled.ShouldBeTrue("Expected approve button to be disabled (stub)");
    }

    [Then(@"the pause button should be disabled")]
    public async Task ThenThePauseButtonShouldBeDisabled()
    {
        var listingDetailPage = new ListingDetailPage(Page, Fixture.WasmBaseUrl);
        var isDisabled = await listingDetailPage.IsPauseButtonDisabledAsync();
        isDisabled.ShouldBeTrue("Expected pause button to be disabled (stub)");
    }

    [Then(@"the end listing button should be disabled")]
    public async Task ThenTheEndListingButtonShouldBeDisabled()
    {
        var listingDetailPage = new ListingDetailPage(Page, Fixture.WasmBaseUrl);
        var isDisabled = await listingDetailPage.IsEndButtonDisabledAsync();
        isDisabled.ShouldBeTrue("Expected end listing button to be disabled (stub)");
    }
}
