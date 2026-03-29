using System.Text.RegularExpressions;
using VendorPortal.E2ETests.Pages;

namespace VendorPortal.E2ETests.Features;

/// <summary>
/// Step definitions for vendor portal team management scenarios.
/// Covers roster display and admin-only gating (executable scenarios).
/// WIP scenarios (invite, role change, deactivate) will be implemented when the UI is ready.
/// </summary>
[Binding]
public sealed class VendorTeamManagementStepDefinitions
{
    private readonly ScenarioContext _scenarioContext;

    public VendorTeamManagementStepDefinitions(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
    }

    private IPage Page => _scenarioContext.Get<IPage>(ScenarioContextKeys.Page);
    private E2ETestFixture Fixture => _scenarioContext.Get<E2ETestFixture>(ScenarioContextKeys.Fixture);

    // ─── Given: Role-Based Authentication ───

    [Given("I am authenticated as a vendor user with Role {string}")]
    public async Task GivenIAmAuthenticatedAsAVendorUserWithRole(string role)
    {
        var email = role switch
        {
            "Admin" => WellKnownVendorTestData.Users.AdminEmail,
            "CatalogManager" => WellKnownVendorTestData.Users.CatalogManagerEmail,
            "ReadOnly" => WellKnownVendorTestData.Users.ReadOnlyEmail,
            _ => throw new ArgumentException($"Unknown test role: {role}")
        };

        var loginPage = new VendorLoginPage(Page);
        await loginPage.NavigateAsync();

        // Wait for WASM hydration — CI environments need generous timeout
        await Page.WaitForSelectorAsync("[data-testid='login-btn']", new PageWaitForSelectorOptions
        {
            Timeout = 60000
        });

        await loginPage.FillEmailAsync(email);
        await loginPage.FillPasswordAsync(WellKnownVendorTestData.Users.SharedPassword);
        await loginPage.ClickSignInAsync();

        // Wait for navigation to dashboard (login redirects there)
        await Page.WaitForURLAsync("**/dashboard", new PageWaitForURLOptions
        {
            Timeout = 30000,
            WaitUntil = WaitUntilState.Commit
        });

        // Wait for dashboard to fully render before proceeding
        await Page.WaitForSelectorAsync("[data-testid='kpi-low-stock-alerts']", new PageWaitForSelectorOptions
        {
            Timeout = 30000,
            State = WaitForSelectorState.Visible
        });
    }

    // ─── When: Navigation ───

    [When("I navigate to {string}")]
    public async Task WhenINavigateTo(string pageName)
    {
        switch (pageName)
        {
            case "Team Management":
                var teamPage = new TeamManagementPage(Page);
                await teamPage.NavigateAsync();
                await teamPage.WaitForLoadedAsync();
                break;
            default:
                throw new ArgumentException($"Unknown page: {pageName}. Add a case to {nameof(WhenINavigateTo)}.");
        }
    }

    // ─── Then: Roster Assertions ───

    [Then("I see a roster of {int} team members")]
    public async Task ThenISeeARosterOfTeamMembers(int expectedCount)
    {
        var teamPage = new TeamManagementPage(Page);
        await teamPage.RosterTable.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });
        var countText = await teamPage.GetMemberCountAsync();

        // Extract the first number from the roster count text (e.g., "3 members" → 3)
        var match = Regex.Match(countText.Trim(), @"\d+");
        match.Success.ShouldBeTrue($"Expected roster count text to contain a number, but got: '{countText}'");
        int.Parse(match.Value).ShouldBe(expectedCount);
    }

    [Then("each member shows their name, email, role, and status")]
    public async Task ThenEachMemberShowsTheirNameEmailRoleAndStatus()
    {
        var teamPage = new TeamManagementPage(Page);

        // Verify the roster table is visible with expected columns
        (await teamPage.RosterTable.IsVisibleAsync()).ShouldBeTrue("Roster table should be visible");

        // Verify that at least one row exists with all expected cell types
        // The table should contain member-name, member-email, member-role, member-status cells
        var nameLocator = Page.Locator("[data-testid^='member-name-']");
        var emailLocator = Page.Locator("[data-testid^='member-email-']");
        var roleLocator = Page.Locator("[data-testid^='member-role-']");
        var statusLocator = Page.Locator("[data-testid^='member-status-']");

        (await nameLocator.CountAsync()).ShouldBeGreaterThan(0, "Expected at least one member name cell");
        (await emailLocator.CountAsync()).ShouldBeGreaterThan(0, "Expected at least one member email cell");
        (await roleLocator.CountAsync()).ShouldBeGreaterThan(0, "Expected at least one member role cell");
        (await statusLocator.CountAsync()).ShouldBeGreaterThan(0, "Expected at least one member status cell");
    }

    [Then("each member shows their last login date")]
    public async Task ThenEachMemberShowsTheirLastLoginDate()
    {
        var lastLoginLocator = Page.Locator("[data-testid^='member-last-login-']");
        (await lastLoginLocator.CountAsync()).ShouldBeGreaterThan(0, "Expected at least one member last login cell");
    }

    // ─── Then: Admin Gating ───

    [Then("I see a message: {string}")]
    public async Task ThenISeeAMessage(string expectedMessage)
    {
        var teamPage = new TeamManagementPage(Page);
        await teamPage.AdminOnlyMessage.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });
        var text = await teamPage.AdminOnlyMessage.InnerTextAsync();
        text.ShouldContain(expectedMessage);
    }

    [Then("no team roster is displayed")]
    public async Task ThenNoTeamRosterIsDisplayed()
    {
        var teamPage = new TeamManagementPage(Page);
        (await teamPage.RosterTable.IsVisibleAsync()).ShouldBeFalse("Roster table should not be visible for non-admin users");
    }
}
