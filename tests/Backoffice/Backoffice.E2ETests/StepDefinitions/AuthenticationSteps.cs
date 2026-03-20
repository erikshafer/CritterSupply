using Backoffice.E2ETests.Pages;
using Microsoft.Playwright;
using Reqnroll;
using Shouldly;

namespace Backoffice.E2ETests.StepDefinitions;

[Binding]
public sealed class AuthenticationSteps
{
    private readonly ScenarioContext _scenarioContext;

    public AuthenticationSteps(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
    }

    private E2ETestFixture Fixture => _scenarioContext.Get<E2ETestFixture>(ScenarioContextKeys.Fixture);
    private IPage Page => _scenarioContext.Get<IPage>(ScenarioContextKeys.Page);

    [Given(@"the Backoffice application is running")]
    public void GivenTheBackofficeApplicationIsRunning()
    {
        // Fixture already initialized by hooks, just verify servers are up
        Fixture.IdentityApiBaseUrl.ShouldNotBeNullOrEmpty();
        Fixture.BackofficeApiBaseUrl.ShouldNotBeNullOrEmpty();
        Fixture.WasmBaseUrl.ShouldNotBeNullOrEmpty();
    }

    [Given(@"I am on the login page")]
    public async Task GivenIAmOnTheLoginPage()
    {
        var loginPage = new LoginPage(Page, Fixture.WasmBaseUrl);
        await loginPage.NavigateAsync();
    }

    [When(@"I log in with email ""(.*)"" and password ""(.*)""")]
    public async Task WhenILogInWithEmailAndPassword(string email, string password)
    {
        var loginPage = new LoginPage(Page, Fixture.WasmBaseUrl);
        await loginPage.LoginAsync(email, password);
    }

    [When(@"I log out")]
    public async Task WhenILogOut()
    {
        // Click logout button in app bar
        var logoutButton = Page.GetByTestId("logout-button");
        await logoutButton.ClickAsync();

        // Wait for redirect to login page
        await Page.WaitForURLAsync(url => url.Contains("/login"), new() { Timeout = 5_000 });
    }

    [When(@"I refresh the page")]
    public async Task WhenIRefreshThePage()
    {
        await Page.ReloadAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    [Given(@"I am logged in as ""(.*)""")]
    public async Task GivenIAmLoggedInAs(string email)
    {
        var loginPage = new LoginPage(Page, Fixture.WasmBaseUrl);
        await loginPage.NavigateAsync();
        await loginPage.LoginAndWaitForDashboardAsync(email, "Password123!");
    }

    [Then(@"I should be redirected to the dashboard")]
    public async Task ThenIShouldBeRedirectedToTheDashboard()
    {
        var dashboardPage = new DashboardPage(Page, Fixture.WasmBaseUrl);
        var isOnDashboard = await dashboardPage.IsOnDashboardPageAsync();
        isOnDashboard.ShouldBeTrue();
    }

    [Then(@"I should see the executive dashboard KPI cards")]
    public async Task ThenIShouldSeeTheExecutiveDashboardKPICards()
    {
        var dashboardPage = new DashboardPage(Page, Fixture.WasmBaseUrl);

        // Wait for at least one KPI card to be visible
        var totalOrders = await dashboardPage.GetTotalOrdersValueAsync();
        totalOrders.ShouldNotBeNull();
    }

    [Then(@"the real-time indicator should show ""(.*)""")]
    public async Task ThenTheRealTimeIndicatorShouldShow(string expectedStatus)
    {
        var dashboardPage = new DashboardPage(Page, Fixture.WasmBaseUrl);

        if (expectedStatus == "Connected")
        {
            var isConnected = await dashboardPage.IsRealtimeConnectedAsync();
            isConnected.ShouldBeTrue();
        }
        else if (expectedStatus == "Disconnected")
        {
            var isDisconnected = await dashboardPage.IsRealtimeDisconnectedAsync();
            isDisconnected.ShouldBeTrue();
        }
    }

    [Then(@"I should remain on the login page")]
    public async Task ThenIShouldRemainOnTheLoginPage()
    {
        var loginPage = new LoginPage(Page, Fixture.WasmBaseUrl);
        var isOnLogin = await loginPage.IsOnLoginPageAsync();
        isOnLogin.ShouldBeTrue();
    }

    [Then(@"I should see an error message ""(.*)""")]
    public async Task ThenIShouldSeeAnErrorMessage(string expectedErrorMessage)
    {
        var loginPage = new LoginPage(Page, Fixture.WasmBaseUrl);
        var errorText = await loginPage.GetErrorTextAsync();
        errorText.ShouldNotBeNull();
        errorText.ShouldContain(expectedErrorMessage);
    }

    [Then(@"I should still be on the dashboard")]
    public async Task ThenIShouldStillBeOnTheDashboard()
    {
        await ThenIShouldBeRedirectedToTheDashboard();
    }
}
