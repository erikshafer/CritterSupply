using System.Net.Http.Json;
using VendorPortal.E2ETests.Pages;

namespace VendorPortal.E2ETests.Features;

/// <summary>
/// Step definitions for vendor portal change request scenarios.
/// Covers P0: submit, save draft. P1a: list, RBAC, logout.
/// </summary>
[Binding]
public sealed class VendorChangeRequestStepDefinitions
{
    private readonly ScenarioContext _scenarioContext;

    public VendorChangeRequestStepDefinitions(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
    }

    private IPage Page => _scenarioContext.Get<IPage>(ScenarioContextKeys.Page);
    private E2ETestFixture Fixture => _scenarioContext.Get<E2ETestFixture>(ScenarioContextKeys.Fixture);

    // ─── When: Navigation ───

    [When("I navigate to the submit change request page")]
    public async Task WhenINavigateToTheSubmitChangeRequestPage()
    {
        var submitPage = new SubmitChangeRequestPage(Page);
        await submitPage.NavigateAsync();
        await Page.WaitForSelectorAsync("[data-testid='sku-field']", new PageWaitForSelectorOptions
        {
            Timeout = 15000
        });
    }

    [When("I navigate to the change requests page")]
    [Given("I navigate to the change requests page")]
    public async Task WhenINavigateToTheChangeRequestsPage()
    {
        var listPage = new ChangeRequestsPage(Page);
        await listPage.NavigateAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    // ─── When: Form Interactions ───

    [When("I fill in SKU {string} title {string} and details {string}")]
    public async Task WhenIFillInTheForm(string sku, string title, string details)
    {
        var submitPage = new SubmitChangeRequestPage(Page);
        await submitPage.FillSkuAsync(sku);
        await submitPage.FillTitleAsync(title);
        await submitPage.FillDetailsAsync(details);
        // Brief pause for Blazor form binding
        await Page.WaitForTimeoutAsync(300);
    }

    [When("I click the submit button")]
    public async Task WhenIClickTheSubmitButton()
    {
        var submitPage = new SubmitChangeRequestPage(Page);
        await submitPage.ClickSubmitAsync();
    }

    [When("I click the save draft button")]
    public async Task WhenIClickTheSaveDraftButton()
    {
        var submitPage = new SubmitChangeRequestPage(Page);
        await submitPage.ClickSaveDraftAsync();
    }

    // ─── Given: Data Setup via API ───

    [Given("I have submitted a change request for SKU {string} with title {string}")]
    public async Task GivenIHaveSubmittedAChangeRequest(string sku, string title)
    {
        // Seed a change request via the submit form (browser-driven, not API)
        // Navigate to submit, fill, and submit
        await WhenINavigateToTheSubmitChangeRequestPage();
        await WhenIFillInTheForm(sku, title, "E2E test change request details");
        await WhenIClickTheSubmitButton();
        // Wait for redirect back to list
        await Page.WaitForURLAsync("**/change-requests", new PageWaitForURLOptions { Timeout = 15000 });
    }

    // ─── Then: Assertions ───

    [Then("I should be redirected to the change requests list")]
    public async Task ThenIShouldBeRedirectedToTheChangeRequestsList()
    {
        await Page.WaitForURLAsync("**/change-requests", new PageWaitForURLOptions { Timeout = 15000 });
        Page.Url.ShouldContain("/change-requests");
    }

    [Then("I should see the change requests table")]
    public async Task ThenIShouldSeeTheChangeRequestsTable()
    {
        var listPage = new ChangeRequestsPage(Page);
        await listPage.ChangeRequestsTable.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });
        (await listPage.IsTableVisibleAsync()).ShouldBeTrue();
    }

    [Then("the table should contain {string}")]
    public async Task ThenTheTableShouldContain(string expectedText)
    {
        var listPage = new ChangeRequestsPage(Page);
        await listPage.ChangeRequestsTable.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });
        var text = await listPage.ChangeRequestsTable.InnerTextAsync();
        text.ShouldContain(expectedText);
    }

    [Then("I should not see the submit change request button on the list page")]
    public async Task ThenIShouldNotSeeTheSubmitButton()
    {
        // Wait for page to fully load
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Page.WaitForTimeoutAsync(1000);

        var listPage = new ChangeRequestsPage(Page);
        (await listPage.SubmitChangeRequestButton.IsVisibleAsync()).ShouldBeFalse();
    }

    // ─── Logout ───

    [When("I click the logout button")]
    public async Task WhenIClickTheLogoutButton()
    {
        await Page.GetByTestId("logout-btn").ClickAsync();
    }

    [Then("I should not see user info in the app bar")]
    public async Task ThenIShouldNotSeeUserInfoInTheAppBar()
    {
        // After logout, the user info element should not be visible
        var userInfo = Page.GetByTestId("user-info-text");
        (await userInfo.IsVisibleAsync()).ShouldBeFalse();
    }
}
