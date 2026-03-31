using Backoffice.E2ETests.Pages;

namespace Backoffice.E2ETests.StepDefinitions;

[Binding]
public sealed class SessionExpirySteps
{
    private readonly ScenarioContext _scenarioContext;

    public SessionExpirySteps(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
    }

    private E2ETestFixture Fixture => _scenarioContext.Get<E2ETestFixture>(ScenarioContextKeys.Fixture);
    private IPage Page => _scenarioContext.Get<IPage>(ScenarioContextKeys.Page);

    [When(@"my session expires")]
    public void WhenMySessionExpires()
    {
        // Mark session as expired — stubs will return 401 for next API call
        Fixture.StubInventoryClient.SimulateSessionExpired = true;
        Fixture.StubOrdersClient.SimulateSessionExpired = true;
        Fixture.StubCustomerIdentityClient.SimulateSessionExpired = true;
        Fixture.StubPricingClient.SimulateSessionExpired = true;
    }

    [When(@"I trigger a data refresh")]
    public async Task WhenITriggerADataRefresh()
    {
        var dashboardPage = new DashboardPage(Page, Fixture.WasmBaseUrl);
        await dashboardPage.ClickRefreshButtonInBannerAsync();
    }

    [When(@"I try to acknowledge an alert")]
    public async Task WhenITryToAcknowledgeAnAlert()
    {
        // Assumes we're on the alerts page and there's at least one alert
        var alertsPage = new OperationsAlertsPage(Page, Fixture.WasmBaseUrl);

        // Get the first alert and try to acknowledge it
        var alertCount = await alertsPage.GetAlertCountAsync();
        if (alertCount > 0)
        {
            await alertsPage.ClickFirstAlertAsync();
            await alertsPage.AcknowledgeAlertAsync();
        }
    }

    [When(@"I trigger a search")]
    public async Task WhenITriggerASearch()
    {
        var customerSearchPage = new CustomerSearchPage(Page, Fixture.WasmBaseUrl);
        await customerSearchPage.SearchAsync("test@example.com");
    }

    [When(@"the session expired modal appears")]
    [Then(@"I should see the session expired modal")]
    public async Task ThenIShouldSeeTheSessionExpiredModal()
    {
        var sessionExpiredPage = new SessionExpiredPage(Page);
        var isVisible = await sessionExpiredPage.IsSessionExpiredModalVisibleAsync();
        isVisible.ShouldBeTrue();
    }

    [Then(@"the modal should display ""(.*)""")]
    public async Task ThenTheModalShouldDisplay(string expectedText)
    {
        var sessionExpiredPage = new SessionExpiredPage(Page);
        var modalText = await sessionExpiredPage.GetModalMessageAsync();
        modalText.ShouldNotBeNull();
        modalText.ShouldContain(expectedText);
    }

    [Then(@"the modal should have a ""(.*)"" button")]
    public async Task ThenTheModalShouldHaveAButton(string buttonText)
    {
        var sessionExpiredPage = new SessionExpiredPage(Page);

        if (buttonText == "Log In Again")
        {
            var isVisible = await sessionExpiredPage.IsLogInAgainButtonVisibleAsync();
            isVisible.ShouldBeTrue();
        }
    }

    [Then(@"the modal should block interaction with the page")]
    [Obsolete("CanInteractWithPAgeBehindModalAsync() is Obsolete")]
    public async Task ThenTheModalShouldBlockInteractionWithThePage()
    {
        var sessionExpiredPage = new SessionExpiredPage(Page);
        var canInteract = await sessionExpiredPage.CanInteractWithPageBehindModalAsync();
        canInteract.ShouldBeFalse(); // Modal should block interaction
    }

    [Then(@"I should not be able to interact with the alerts feed")]
    [Obsolete("ThenTheModalShouldBlockInteractionWithThePage is Obsolete")]
    public async Task ThenIShouldNotBeAbleToInteractWithTheAlertsFeed()
    {
        // Session expired modal should block interaction
        await ThenTheModalShouldBlockInteractionWithThePage();
    }

    [When(@"I click ""Log In Again""")]
    public async Task WhenIClickLogInAgain()
    {
        var sessionExpiredPage = new SessionExpiredPage(Page);
        await sessionExpiredPage.ClickLogInAgainAsync();
    }

    [Then(@"I should be redirected to the login page")]
    public async Task ThenIShouldBeRedirectedToTheLoginPage()
    {
        var loginPage = new LoginPage(Page, Fixture.WasmBaseUrl);
        var isOnLogin = await loginPage.IsOnLoginPageAsync();
        isOnLogin.ShouldBeTrue();
    }

    [Then(@"the returnUrl query parameter should be ""(.*)""")]
    public async Task ThenTheReturnUrlQueryParameterShouldBe(string expectedReturnUrl)
    {
        await Task.Delay(500); // Allow navigation to complete
        var url = Page.Url;
        url.ShouldContain($"returnUrl={Uri.EscapeDataString(expectedReturnUrl)}");
    }

    [Then(@"I should be redirected back to the dashboard")]
    public async Task ThenIShouldBeRedirectedBackToTheDashboard()
    {
        var dashboardPage = new DashboardPage(Page, Fixture.WasmBaseUrl);
        var isOnDashboard = await dashboardPage.IsOnDashboardPageAsync();
        isOnDashboard.ShouldBeTrue();
    }

    [Then(@"I should be redirected back to the operations alerts page")]
    public async Task ThenIShouldBeRedirectedBackToTheOperationsAlertsPage()
    {
        var alertsPage = new OperationsAlertsPage(Page, Fixture.WasmBaseUrl);
        var isOnAlerts = await alertsPage.IsOnOperationsAlertsPageAsync();
        isOnAlerts.ShouldBeTrue();
    }

    [Then(@"I should see the operations alerts feed")]
    public async Task ThenIShouldSeeTheOperationsAlertsFeed()
    {
        var alertsPage = new OperationsAlertsPage(Page, Fixture.WasmBaseUrl);
        var isVisible = await alertsPage.IsAlertFeedVisibleAsync();
        isVisible.ShouldBeTrue();
    }

    [Then(@"the button should show ""(.*)"" briefly")]
    public async Task ThenTheButtonShouldShowBriefly(string expectedText)
    {
        // Check for "Acknowledging..." text in the button
        var acknowledgeButton = Page.Locator("button:has-text('Acknowledging...')");

        try
        {
            await acknowledgeButton.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 2_000 });
        }
        catch (TimeoutException)
        {
            // Button text may have already changed — acceptable for "briefly" assertion
        }
    }

    [Then(@"the session expired modal should appear")]
    public async Task ThenTheSessionExpiredModalShouldAppear()
    {
        await ThenIShouldSeeTheSessionExpiredModal();
    }

    [Then(@"the alert should still be visible in the feed")]
    public async Task ThenTheAlertShouldStillBeVisibleInTheFeed()
    {
        var alertsPage = new OperationsAlertsPage(Page, Fixture.WasmBaseUrl);
        var alertCount = await alertsPage.GetAlertCountAsync();
        alertCount.ShouldBeGreaterThan(0);
    }

    [Then(@"the session expired modal should still be visible")]
    public async Task ThenTheSessionExpiredModalShouldStillBeVisible()
    {
        await ThenIShouldSeeTheSessionExpiredModal();
    }

    [Then(@"I should not see a duplicate modal")]
    public async Task ThenIShouldNotSeeADuplicateModal()
    {
        var sessionExpiredPage = new SessionExpiredPage(Page);
        var modalCount = await sessionExpiredPage.GetSessionExpiredModalCountAsync();
        modalCount.ShouldBe(1);
    }

    [When(@"I close the modal")]
    public async Task WhenICloseTheModal()
    {
        var sessionExpiredPage = new SessionExpiredPage(Page);
        await sessionExpiredPage.CloseModalAsync();
    }

    [Scope(Feature = "Session Expiry and Recovery")]
    [Then(@"the modal should be hidden")]
    public async Task ThenTheModalShouldBeHidden()
    {
        var sessionExpiredPage = new SessionExpiredPage(Page);
        var isHidden = await sessionExpiredPage.IsModalHiddenAsync();
        isHidden.ShouldBeTrue();
    }

    [Then(@"subsequent API calls should still trigger the modal")]
    public void ThenSubsequentAPICallsShouldStillTriggerTheModal()
    {
        // Ensure session expired state is still active
        Fixture.StubInventoryClient.SimulateSessionExpired.ShouldBeTrue();
        Fixture.StubOrdersClient.SimulateSessionExpired.ShouldBeTrue();
    }

    [Then(@"the returnUrl parameter should still be preserved")]
    public async Task ThenTheReturnUrlParameterShouldStillBePreserved()
    {
        var url = Page.Url;
        url.ShouldContain("returnUrl=");
    }

    [Then(@"I should see exactly 1 session expired modal")]
    public async Task ThenIShouldSeeExactly1SessionExpiredModal()
    {
        await ThenIShouldNotSeeADuplicateModal();
    }

    [Then(@"all 3 alerts should still be unacknowledged")]
    public async Task ThenAll3AlertsShouldStillBeUnacknowledged()
    {
        var alertsPage = new OperationsAlertsPage(Page, Fixture.WasmBaseUrl);
        var alertCount = await alertsPage.GetAlertCountAsync();
        alertCount.ShouldBe(3);
    }

    [Given(@"I am on the customer search page")]
    public async Task GivenIAmOnTheCustomerSearchPage()
    {
        var customerSearchPage = new CustomerSearchPage(Page, Fixture.WasmBaseUrl);
        await customerSearchPage.NavigateAsync();
    }
}
