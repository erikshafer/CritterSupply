using Backoffice.E2ETests.Pages;
using Microsoft.Playwright;
using Reqnroll;
using Shouldly;
using Backoffice.Clients;

namespace Backoffice.E2ETests.StepDefinitions;

[Binding]
public sealed class ReturnManagementSteps
{
    private readonly ScenarioContext _scenarioContext;

    public ReturnManagementSteps(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
    }

    private E2ETestFixture Fixture => _scenarioContext.Get<E2ETestFixture>(ScenarioContextKeys.Fixture);
    private IPage Page => _scenarioContext.Get<IPage>(ScenarioContextKeys.Page);

    [Given(@"I am on the dashboard page")]
    public async Task GivenIAmOnTheDashboardPage()
    {
        var dashboardPage = new DashboardPage(Page, Fixture.WasmBaseUrl);
        var isOnDashboard = await dashboardPage.IsOnDashboardPageAsync();

        if (!isOnDashboard)
        {
            await dashboardPage.NavigateAsync();
        }
    }

    // Given Steps - Test Data Seeding
    [Given(@"(\d+) returns exist with status ""(.*)""")]
    public void GivenReturnsExistWithStatus(int count, string status)
    {
        for (int i = 0; i < count; i++)
        {
            var returnId = Guid.NewGuid();
            var orderId = Guid.NewGuid();
            var customerId = Guid.NewGuid();

            Fixture.StubReturnsClient.AddReturn(
                returnId,
                orderId,
                customerId,
                status,
                DateTimeOffset.UtcNow.AddDays(-i - 1)); // Stagger dates for realistic ordering
        }
    }

    [Given(@"(\d+) return exists with status ""(.*)""")]
    public void GivenOneReturnExistsWithStatus(int count, string status)
    {
        // Singular form delegates to plural
        GivenReturnsExistWithStatus(count, status);
    }

    [Given(@"(\d+) more returns are created with status ""(.*)""")]
    public void GivenMoreReturnsAreCreatedWithStatus(int count, string status)
    {
        // Adds additional returns to simulate real-time updates
        GivenReturnsExistWithStatus(count, status);
    }

    // When Steps - Navigation
    [When(@"I navigate to the ""(.*)"" page")]
    public async Task WhenINavigateToThePage(string pagePath)
    {
        if (pagePath == "/returns")
        {
            var returnManagementPage = new ReturnManagementPage(Page, Fixture.WasmBaseUrl);
            await returnManagementPage.NavigateAsync();
        }
        else
        {
            await Page.GotoAsync($"{Fixture.WasmBaseUrl}{pagePath}", new PageGotoOptions { WaitUntil = WaitUntilState.Commit });
            await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        }
    }

    [When(@"I click on the ""Return Management"" navigation link")]
    public async Task WhenIClickOnTheReturnManagementNavigationLink()
    {
        var returnManagementPage = new ReturnManagementPage(Page, Fixture.WasmBaseUrl);
        await returnManagementPage.NavigateFromDashboardAsync();
    }

    [When(@"I navigate to the dashboard page")]
    public async Task WhenINavigateToTheDashboardPage()
    {
        var dashboardPage = new DashboardPage(Page, Fixture.WasmBaseUrl);
        await dashboardPage.NavigateAsync();
    }

    // When Steps - Filtering
    [When(@"I select ""(.*)"" from the status filter")]
    public async Task WhenISelectFromTheStatusFilter(string status)
    {
        var returnManagementPage = new ReturnManagementPage(Page, Fixture.WasmBaseUrl);
        await returnManagementPage.SelectStatusFilterAsync(status);
    }

    [When(@"I click the ""Refresh"" button")]
    public async Task WhenIClickTheRefreshButton()
    {
        var returnManagementPage = new ReturnManagementPage(Page, Fixture.WasmBaseUrl);
        await returnManagementPage.ClickLoadReturnsButtonAsync();
    }

    // Then Steps - Page State
    [Then(@"I should be on the ""(.*)"" page")]
    public async Task ThenIShouldBeOnThePage(string pagePath)
    {
        if (pagePath == "/returns")
        {
            var returnManagementPage = new ReturnManagementPage(Page, Fixture.WasmBaseUrl);
            var isOnPage = await returnManagementPage.IsOnReturnManagementPageAsync();
            isOnPage.ShouldBeTrue($"Expected to be on {pagePath}, but URL is: {Page.Url}");
        }
        else
        {
            Page.Url.ShouldContain(pagePath);
        }
    }

    [Then(@"I should see the page heading ""(.*)""")]
    public async Task ThenIShouldSeeThePageHeading(string expectedHeading)
    {
        var returnManagementPage = new ReturnManagementPage(Page, Fixture.WasmBaseUrl);
        var headingText = await returnManagementPage.GetPageHeadingTextAsync();
        headingText.ShouldNotBeNull();
        headingText.ShouldContain(expectedHeading);
    }

    [Then(@"I should see the status filter dropdown")]
    public async Task ThenIShouldSeeTheStatusFilterDropdown()
    {
        var statusFilter = Page.GetByTestId("status-filter");
        var isVisible = await statusFilter.IsVisibleAsync();
        isVisible.ShouldBeTrue();
    }

    // Then Steps - Filter State
    [Then(@"I should see the status filter set to ""(.*)""")]
    public async Task ThenIShouldSeeTheStatusFilterSetTo(string expectedStatus)
    {
        var returnManagementPage = new ReturnManagementPage(Page, Fixture.WasmBaseUrl);
        var selectedValue = await returnManagementPage.GetSelectedStatusFilterValueAsync();
        selectedValue.ShouldBe(expectedStatus);
    }

    // Then Steps - Returns List
    [Then(@"I should see (\d+) returns? in the table")]
    public async Task ThenIShouldSeeReturnsInTheTable(int expectedCount)
    {
        var returnManagementPage = new ReturnManagementPage(Page, Fixture.WasmBaseUrl);
        var actualCount = await returnManagementPage.GetReturnCountAsync();
        actualCount.ShouldBe(expectedCount, $"Expected {expectedCount} returns, but found {actualCount}");
    }

    [When(@"I see (\d+) returns in the table")]
    public async Task WhenISeeReturnsInTheTable(int expectedCount)
    {
        // Same as Then step (reused for intermediate state checks)
        await ThenIShouldSeeReturnsInTheTable(expectedCount);
    }

    [Then(@"the return count badge should show ""(.*)""")]
    public async Task ThenTheReturnCountBadgeShouldShow(string expectedText)
    {
        var returnManagementPage = new ReturnManagementPage(Page, Fixture.WasmBaseUrl);
        var badgeText = await returnManagementPage.GetReturnCountBadgeTextAsync();
        badgeText.ShouldNotBeNull();
        badgeText.ShouldContain(expectedText);
    }

    [Then(@"the return count badge should not be visible")]
    public async Task ThenTheReturnCountBadgeShouldNotBeVisible()
    {
        var returnManagementPage = new ReturnManagementPage(Page, Fixture.WasmBaseUrl);
        var isVisible = await returnManagementPage.IsReturnCountBadgeVisibleAsync();
        isVisible.ShouldBeFalse();
    }

    [Then(@"all displayed returns should have status ""(.*)""")]
    public async Task ThenAllDisplayedReturnsShouldHaveStatus(string expectedStatus)
    {
        var returnManagementPage = new ReturnManagementPage(Page, Fixture.WasmBaseUrl);
        var statuses = await returnManagementPage.GetDisplayedReturnStatusesAsync();

        statuses.Count.ShouldBeGreaterThan(0, "No returns displayed to verify status");
        statuses.ShouldAllBe(s => s.Equals(expectedStatus, StringComparison.OrdinalIgnoreCase),
            $"All returns should have status '{expectedStatus}'");
    }

    // Then Steps - Empty State
    [Then(@"I should see the ""no returns"" alert")]
    public async Task ThenIShouldSeeTheNoReturnsAlert()
    {
        var returnManagementPage = new ReturnManagementPage(Page, Fixture.WasmBaseUrl);
        var isVisible = await returnManagementPage.IsNoReturnsAlertVisibleAsync();
        isVisible.ShouldBeTrue();
    }

    [Then(@"the return count should show (\d+)")]
    public async Task ThenTheReturnCountShouldShow(int expectedCount)
    {
        var returnManagementPage = new ReturnManagementPage(Page, Fixture.WasmBaseUrl);
        var actualCount = await returnManagementPage.GetReturnCountAsync();
        actualCount.ShouldBe(expectedCount);
    }

    [Then(@"I should not see the returns table")]
    public async Task ThenIShouldNotSeeTheReturnsTable()
    {
        var returnManagementPage = new ReturnManagementPage(Page, Fixture.WasmBaseUrl);
        var isVisible = await returnManagementPage.IsReturnsTableVisibleAsync();
        isVisible.ShouldBeFalse();
    }

    // Then Steps - Dashboard KPI Integration
    [Then(@"the ""Pending Returns"" KPI should show ""(.*)""")]
    public async Task ThenThePendingReturnsKPIShouldShow(string expectedValue)
    {
        var dashboardPage = new DashboardPage(Page, Fixture.WasmBaseUrl);
        var kpiValue = await dashboardPage.GetPendingReturnsValueAsync();
        kpiValue.ShouldNotBeNull();
        kpiValue.Trim().ShouldBe(expectedValue);
    }

    // Then Steps - Authorization
    [Given(@"I am logged out")]
    public async Task GivenIAmLoggedOut()
    {
        // Navigate to login page (clears any existing session)
        var loginPage = new LoginPage(Page, Fixture.WasmBaseUrl);
        await loginPage.NavigateAsync();
    }

    [Given(@"I log in with (.*) role")]
    public async Task GivenILogInWithRole(string role)
    {
        var loginPage = new LoginPage(Page, Fixture.WasmBaseUrl);

        // Map role to test email
        string email = role switch
        {
            "customer-service" => "cs-admin@crittersupply.com",
            "operations-manager" => "ops-manager@crittersupply.com",
            "system-admin" => "admin@crittersupply.com",
            _ => throw new ArgumentException($"Unknown role: {role}")
        };

        await loginPage.LoginAndWaitForDashboardAsync(email, "Password123!");
    }

    [Then(@"I should see the Return Management page")]
    public async Task ThenIShouldSeeTheReturnManagementPage()
    {
        var returnManagementPage = new ReturnManagementPage(Page, Fixture.WasmBaseUrl);
        var isOnPage = await returnManagementPage.IsOnReturnManagementPageAsync();
        var isHeadingVisible = await returnManagementPage.IsPageHeadingVisibleAsync();

        isOnPage.ShouldBeTrue();
        isHeadingVisible.ShouldBeTrue();
    }

    [Then(@"I should not see an authorization error")]
    public async Task ThenIShouldNotSeeAnAuthorizationError()
    {
        var returnManagementPage = new ReturnManagementPage(Page, Fixture.WasmBaseUrl);
        var hasAuthError = await returnManagementPage.IsAuthorizationErrorVisibleAsync();
        hasAuthError.ShouldBeFalse();
    }

    [Then(@"I should not see updated return data")]
    public async Task ThenIShouldNotSeeUpdatedReturnData()
    {
        // After session expiry, the API call fails, so returns count should not increase
        // This is implicit in the session expired modal appearing
        var returnManagementPage = new ReturnManagementPage(Page, Fixture.WasmBaseUrl);
        var isModalVisible = await returnManagementPage.IsSessionExpiredModalVisibleAsync();
        isModalVisible.ShouldBeTrue();
    }

    // --- Return Detail Page Steps ---

    [Given(@"(\d+) return exists with status ""(.*)"" and ID stored as ""(.*)""")]
    public void GivenReturnExistsWithStatusAndIdStored(int count, string status, string contextKey)
    {
        var returnId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        Fixture.StubReturnsClient.AddReturn(
            returnId,
            orderId,
            customerId,
            status,
            DateTimeOffset.UtcNow.AddDays(-1),
            new ReturnItemDto("SKU-001", "Organic Dog Treats", 2, "New"));

        _scenarioContext[ScenarioContextKeys.ReturnId] = returnId;
    }

    [When(@"I click View Details on the stored return")]
    public async Task WhenIClickViewDetailsOnTheStoredReturn()
    {
        var returnId = _scenarioContext.Get<Guid>(ScenarioContextKeys.ReturnId);
        var viewDetailsButton = Page.GetByTestId($"view-return-{returnId}");

        await viewDetailsButton.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        await viewDetailsButton.ClickAsync();

        // Wait for WASM client-side navigation
        await Page.WaitForURLAsync(
            url => url.Contains($"/returns/{returnId}"),
            new() { Timeout = 5_000, WaitUntil = WaitUntilState.Commit });

        var returnDetailPage = new ReturnDetailPage(Page, Fixture.WasmBaseUrl);
        await returnDetailPage.WaitForPageLoadedAsync();
    }

    [When(@"I navigate to the return detail page for the stored return")]
    public async Task WhenINavigateToTheReturnDetailPageForTheStoredReturn()
    {
        var returnId = _scenarioContext.Get<Guid>(ScenarioContextKeys.ReturnId);
        var returnDetailPage = new ReturnDetailPage(Page, Fixture.WasmBaseUrl);
        await returnDetailPage.NavigateAsync(returnId);
    }

    [Then(@"I should be on the return detail page")]
    public async Task ThenIShouldBeOnTheReturnDetailPage()
    {
        var returnDetailPage = new ReturnDetailPage(Page, Fixture.WasmBaseUrl);
        var isOnPage = await returnDetailPage.IsOnReturnDetailPageAsync();
        isOnPage.ShouldBeTrue($"Expected to be on a return detail page, but URL is: {Page.Url}");
    }

    [Then(@"the return status should be ""(.*)""")]
    public async Task ThenTheReturnStatusShouldBe(string expectedStatus)
    {
        var returnDetailPage = new ReturnDetailPage(Page, Fixture.WasmBaseUrl);
        var actualStatus = await returnDetailPage.GetReturnStatusAsync();
        actualStatus.ShouldNotBeNull();
        actualStatus.ShouldContain(expectedStatus);
    }

    [Then(@"the return reason should be visible")]
    public async Task ThenTheReturnReasonShouldBeVisible()
    {
        var returnReason = Page.GetByTestId("return-reason");
        var isVisible = await returnReason.IsVisibleAsync();
        isVisible.ShouldBeTrue();
    }

    [Then(@"I should see the approve button")]
    public async Task ThenIShouldSeeTheApproveButton()
    {
        var returnDetailPage = new ReturnDetailPage(Page, Fixture.WasmBaseUrl);
        var isVisible = await returnDetailPage.IsApproveButtonVisibleAsync();
        isVisible.ShouldBeTrue();
    }

    [Then(@"I should see the deny button")]
    public async Task ThenIShouldSeeTheDenyButton()
    {
        var returnDetailPage = new ReturnDetailPage(Page, Fixture.WasmBaseUrl);
        var isVisible = await returnDetailPage.IsDenyButtonVisibleAsync();
        isVisible.ShouldBeTrue();
    }

    [When(@"I click the approve button")]
    public async Task WhenIClickTheApproveButton()
    {
        var returnDetailPage = new ReturnDetailPage(Page, Fixture.WasmBaseUrl);
        await returnDetailPage.ClickApproveAsync();
    }

    [When(@"I deny the return with reason ""(.*)""")]
    public async Task WhenIDenyTheReturnWithReason(string reason)
    {
        var returnDetailPage = new ReturnDetailPage(Page, Fixture.WasmBaseUrl);
        await returnDetailPage.ClickDenyAsync(reason);
    }
}
