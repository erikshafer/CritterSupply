using Backoffice.E2ETests.Pages;

namespace Backoffice.E2ETests.StepDefinitions;

/// <summary>
/// Step definitions for Listings Admin E2E scenarios.
/// Covers ListingsAdmin.feature (3 executable scenarios) and shared navigation steps
/// used by ListingsDetail.feature.
///
/// M36.1 Session 5: Initial step definitions for listings E2E coverage.
/// Uses StubListingsApiHost for test data seeding — no real Listings.Api required.
/// All selectors target data-testid attributes confirmed in Session 4 retrospective.
/// </summary>
[Binding]
public sealed class ListingsAdminSteps
{
    private readonly ScenarioContext _scenarioContext;

    public ListingsAdminSteps(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
    }

    private E2ETestFixture Fixture => _scenarioContext.Get<E2ETestFixture>(ScenarioContextKeys.Fixture);
    private IPage Page => _scenarioContext.Get<IPage>(ScenarioContextKeys.Page);

    // ─── Given Steps ────────────────────────────────────────────────────────

    [Given(@"test listings exist in the Listings service")]
    public void GivenTestListingsExistInTheListingsService()
    {
        // Seed well-known listings into the stub Listings API host.
        // StubListingsApiHost serves these via GET /api/listings/all and GET /api/listings/{id}.
        Fixture.StubListingsApi.SeedListing(
            WellKnownTestData.Listings.LiveListing,
            WellKnownTestData.Listings.LiveListingSku,
            WellKnownTestData.Listings.LiveListingChannel,
            WellKnownTestData.Listings.LiveListingProductName,
            WellKnownTestData.Listings.LiveListingStatus,
            content: "Premium ceramic feeding bowl for dogs",
            activatedAt: DateTimeOffset.UtcNow.AddDays(-1));

        Fixture.StubListingsApi.SeedListing(
            WellKnownTestData.Listings.DraftListing,
            WellKnownTestData.Listings.DraftListingSku,
            WellKnownTestData.Listings.DraftListingChannel,
            WellKnownTestData.Listings.DraftListingProductName,
            WellKnownTestData.Listings.DraftListingStatus,
            content: "Interactive laser toy for cats");
    }

    [Given(@"a listing exists in ""(.*)"" status")]
    public void GivenAListingExistsInStatus(string status)
    {
        // Seed the appropriate well-known listing based on requested status.
        // Also seed the live listing so the admin table has something to show.
        Fixture.StubListingsApi.SeedListing(
            WellKnownTestData.Listings.LiveListing,
            WellKnownTestData.Listings.LiveListingSku,
            WellKnownTestData.Listings.LiveListingChannel,
            WellKnownTestData.Listings.LiveListingProductName,
            WellKnownTestData.Listings.LiveListingStatus,
            content: "Premium ceramic feeding bowl for dogs",
            activatedAt: DateTimeOffset.UtcNow.AddDays(-1));

        var listingId = status switch
        {
            "ReadyForReview" => WellKnownTestData.Listings.ReadyForReviewListing,
            "Live" => WellKnownTestData.Listings.LiveListing,
            _ => throw new ArgumentException($"No well-known listing for status '{status}'")
        };

        if (status == "ReadyForReview")
        {
            Fixture.StubListingsApi.SeedListing(
                WellKnownTestData.Listings.ReadyForReviewListing,
                WellKnownTestData.Listings.ReadyForReviewListingSku,
                WellKnownTestData.Listings.ReadyForReviewListingChannel,
                WellKnownTestData.Listings.ReadyForReviewListingProductName,
                WellKnownTestData.Listings.ReadyForReviewListingStatus,
                content: "Premium ceramic feeding bowl — ready for review");
        }

        // Store the listing ID for subsequent navigation steps
        _scenarioContext[ScenarioContextKeys.ListingId] = listingId;
    }

    [Given(@"I am on the listings admin page")]
    public async Task GivenIAmOnTheListingsAdminPage()
    {
        var listingsAdminPage = new ListingsAdminPage(Page, Fixture.WasmBaseUrl);
        await listingsAdminPage.NavigateAsync();
    }

    [Given(@"I can see a listing with a known ID")]
    public async Task GivenICanSeeAListingWithAKnownID()
    {
        var listingsAdminPage = new ListingsAdminPage(Page, Fixture.WasmBaseUrl);
        var rowCount = await listingsAdminPage.GetRowCountAsync();
        rowCount.ShouldBeGreaterThan(0, "Expected at least one listing row to be visible");

        // Store the well-known Live listing ID for subsequent steps
        _scenarioContext[ScenarioContextKeys.ListingId] = WellKnownTestData.Listings.LiveListing;
    }

    // ─── When Steps ─────────────────────────────────────────────────────────

    [When(@"I navigate to the listings admin page")]
    public async Task WhenINavigateToTheListingsAdminPage()
    {
        var listingsAdminPage = new ListingsAdminPage(Page, Fixture.WasmBaseUrl);
        await listingsAdminPage.NavigateAsync();
    }

    [When(@"I navigate to the listing detail page")]
    public async Task WhenINavigateToTheListingDetailPage()
    {
        var listingId = _scenarioContext.Get<Guid>(ScenarioContextKeys.ListingId);
        var listingDetailPage = new ListingDetailPage(Page, Fixture.WasmBaseUrl);
        await listingDetailPage.NavigateAsync(listingId.ToString());
    }

    [When(@"I click the ""(.*)"" button")]
    public async Task WhenIClickTheButton(string buttonName)
    {
        var listingDetailPage = new ListingDetailPage(Page, Fixture.WasmBaseUrl);
        switch (buttonName)
        {
            case "Approve":
                await listingDetailPage.ClickApproveButtonAsync();
                break;
            case "Pause":
                await listingDetailPage.ClickPauseButtonAsync();
                break;
            case "End Listing":
                await listingDetailPage.ClickEndButtonAsync();
                break;
            default:
                throw new ArgumentException($"Unknown button: '{buttonName}'");
        }
    }

    [When(@"I filter listings by status ""(.*)""")]
    public async Task WhenIFilterListingsByStatus(string status)
    {
        var listingsAdminPage = new ListingsAdminPage(Page, Fixture.WasmBaseUrl);
        await listingsAdminPage.FilterByStatusAsync(status);
    }

    [When(@"I click on the listing row")]
    public async Task WhenIClickOnTheListingRow()
    {
        var listingId = _scenarioContext.Get<Guid>(ScenarioContextKeys.ListingId);
        var listingsAdminPage = new ListingsAdminPage(Page, Fixture.WasmBaseUrl);
        await listingsAdminPage.ClickViewListingAsync(listingId.ToString());
    }

    // ─── Then Steps ─────────────────────────────────────────────────────────

    [Then(@"I should see the listings table")]
    public async Task ThenIShouldSeeTheListingsTable()
    {
        var listingsAdminPage = new ListingsAdminPage(Page, Fixture.WasmBaseUrl);
        await listingsAdminPage.WaitForLoadedAsync();

        // Verify the listings-table data-testid element is visible
        var table = Page.GetByTestId("listings-table");
        var isVisible = await table.IsVisibleAsync();
        isVisible.ShouldBeTrue("Expected listings-table to be visible");
    }

    [Then(@"I should see at least one listing row")]
    public async Task ThenIShouldSeeAtLeastOneListingRow()
    {
        var listingsAdminPage = new ListingsAdminPage(Page, Fixture.WasmBaseUrl);
        var rowCount = await listingsAdminPage.GetRowCountAsync();
        rowCount.ShouldBeGreaterThan(0, "Expected at least one listing row");
    }

    [Then(@"I should see only listings with status ""(.*)""")]
    public async Task ThenIShouldSeeOnlyListingsWithStatus(string expectedStatus)
    {
        // After filtering, verify all visible rows have the expected status.
        // The ListingStatusBadge component renders with data-testid="listing-status-{status}".
        var listingsAdminPage = new ListingsAdminPage(Page, Fixture.WasmBaseUrl);
        var rowCount = await listingsAdminPage.GetRowCountAsync();
        rowCount.ShouldBeGreaterThan(0, $"Expected at least one listing with status '{expectedStatus}'");

        // Verify each visible status badge matches the filter
        var statusBadges = Page.Locator("[data-testid^='listing-status-']");
        var badgeCount = await statusBadges.CountAsync();

        for (var i = 0; i < badgeCount; i++)
        {
            var badgeText = await statusBadges.Nth(i).TextContentAsync();
            badgeText.ShouldNotBeNull();
            badgeText!.Trim().ToLowerInvariant().ShouldBe(expectedStatus.ToLowerInvariant(),
                $"Row {i} status badge should be '{expectedStatus}' but was '{badgeText}'");
        }
    }

    [Then(@"I should be on the listing detail page")]
    public async Task ThenIShouldBeOnTheListingDetailPage()
    {
        var listingId = _scenarioContext.Get<Guid>(ScenarioContextKeys.ListingId);
        var listingDetailPage = new ListingDetailPage(Page, Fixture.WasmBaseUrl);

        // Wait for detail page to load
        await listingDetailPage.WaitForLoadedAsync();

        var isOnDetailPage = listingDetailPage.IsOnDetailPage(listingId.ToString());
        isOnDetailPage.ShouldBeTrue($"Expected to be on detail page for listing {listingId}");
    }
}
