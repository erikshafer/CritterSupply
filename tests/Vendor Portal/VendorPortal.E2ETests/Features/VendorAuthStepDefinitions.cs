using VendorPortal.E2ETests.Pages;

namespace VendorPortal.E2ETests.Features;

/// <summary>
/// Step definitions for vendor portal authentication scenarios.
/// Covers P0: login (valid/invalid), protected route redirect.
/// </summary>
[Binding]
public sealed class VendorAuthStepDefinitions
{
    private readonly ScenarioContext _scenarioContext;

    public VendorAuthStepDefinitions(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
    }

    private IPage Page => _scenarioContext.Get<IPage>(ScenarioContextKeys.Page);
    private E2ETestFixture Fixture => _scenarioContext.Get<E2ETestFixture>(ScenarioContextKeys.Fixture);

    [When("I navigate to the login page")]
    public async Task WhenINavigateToTheLoginPage()
    {
        var loginPage = new VendorLoginPage(Page);
        await loginPage.NavigateAsync();
        // Wait for WASM to hydrate — Blazor WASM needs .NET runtime loaded
        await Page.WaitForSelectorAsync("[data-testid='login-btn']", new PageWaitForSelectorOptions
        {
            Timeout = 30000 // WASM cold start can take up to 30s
        });
    }

    [When("I enter {string} as email and {string} as password")]
    public async Task WhenIEnterCredentials(string email, string password)
    {
        var loginPage = new VendorLoginPage(Page);
        await loginPage.FillEmailAsync(email);
        await loginPage.FillPasswordAsync(password);
    }

    [When("I click the sign in button")]
    public async Task WhenIClickTheSignInButton()
    {
        var loginPage = new VendorLoginPage(Page);
        await loginPage.ClickSignInAsync();
    }

    [Then("I should be redirected to the dashboard")]
    public async Task ThenIShouldBeRedirectedToTheDashboard()
    {
        // Wait for navigation to dashboard — Blazor WASM client-side routing (no full page load)
        // Use WaitUntil.Commit to wait for URL change only, not page Load event
        await Page.WaitForURLAsync("**/dashboard", new PageWaitForURLOptions
        {
            Timeout = 15000,
            WaitUntil = WaitUntilState.Commit // Client-side routing doesn't trigger Load event
        });
        Page.Url.ShouldContain("/dashboard");
    }

    [Then("I should see the user info {string} in the app bar")]
    public async Task ThenIShouldSeeTheUserInfoInTheAppBar(string expectedName)
    {
        var userInfo = Page.GetByTestId("user-info-text");
        await userInfo.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });
        var text = await userInfo.InnerTextAsync();
        text.ShouldContain(expectedName);
    }

    [Then("I should see the login error {string}")]
    public async Task ThenIShouldSeeTheLoginError(string expectedError)
    {
        var loginPage = new VendorLoginPage(Page);
        await Page.GetByTestId("login-error-alert").WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });
        var isVisible = await loginPage.IsErrorAlertVisibleAsync();
        isVisible.ShouldBeTrue();
        var errorMessage = await loginPage.GetErrorMessageAsync();
        errorMessage.ShouldContain(expectedError);
    }

    [Then("I should still be on the login page")]
    public void ThenIShouldStillBeOnTheLoginPage()
    {
        Page.Url.ShouldContain("/login");
    }

    [When("I navigate to the dashboard without logging in")]
    public async Task WhenINavigateToTheDashboardWithoutLoggingIn()
    {
        await Page.GotoAsync("/dashboard");
        // Wait for Blazor WASM to load and process the auth redirect
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    [Then("I should be on the login page")]
    public async Task ThenIShouldBeOnTheLoginPage()
    {
        // Blazor WASM client-side routing — wait for URL commit, not full page load
        await Page.WaitForURLAsync("**/login**", new PageWaitForURLOptions
        {
            Timeout = 15000,
            WaitUntil = WaitUntilState.Commit
        });
        Page.Url.ShouldContain("/login");
    }
}
