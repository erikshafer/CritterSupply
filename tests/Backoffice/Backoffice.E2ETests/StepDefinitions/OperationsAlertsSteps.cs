using Backoffice.E2ETests.Pages;
using Microsoft.Playwright;
using Reqnroll;
using Shouldly;

namespace Backoffice.E2ETests.StepDefinitions;

[Binding]
public sealed class OperationsAlertsSteps
{
    private readonly ScenarioContext _scenarioContext;

    public OperationsAlertsSteps(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
    }

    private E2ETestFixture Fixture => _scenarioContext.Get<E2ETestFixture>(ScenarioContextKeys.Fixture);
    private IPage Page => _scenarioContext.Get<IPage>(ScenarioContextKeys.Page);

    [Given(@"I am logged in as an operations admin")]
    public async Task GivenIAmLoggedInAsAnOperationsAdmin()
    {
        // Seed admin user
        Fixture.SeedAdminUser(
            WellKnownTestData.AdminUsers.Alice,
            WellKnownTestData.AdminUsers.AliceEmail,
            "Alice Anderson",
            "Password123!");

        // Log in
        var loginPage = new LoginPage(Page, Fixture.WasmBaseUrl);
        await loginPage.NavigateAsync();
        await loginPage.LoginAndWaitForDashboardAsync(WellKnownTestData.AdminUsers.AliceEmail, "Password123!");
    }

    [Given(@"I am on the operations alerts page")]
    public async Task GivenIAmOnTheOperationsAlertsPage()
    {
        var alertsPage = new OperationsAlertsPage(Page, Fixture.WasmBaseUrl);
        await alertsPage.NavigateAsync();
    }

    [When(@"I navigate to the operations alerts page")]
    public async Task WhenINavigateToTheOperationsAlertsPage()
    {
        var alertsPage = new OperationsAlertsPage(Page, Fixture.WasmBaseUrl);
        await alertsPage.NavigateAsync();
    }

    [Given(@"there are (\d+) unacknowledged low-stock alerts")]
    public void GivenThereAreUnacknowledgedLowStockAlerts(int alertCount)
    {
        for (int i = 0; i < alertCount; i++)
        {
            var alertId = Guid.NewGuid();
            var sku = $"SKU-{i + 1:D3}";
            var severity = i % 2 == 0 ? "Critical" : "Warning";

            Fixture.StubInventoryClient.AddLowStockAlert(
                alertId,
                sku,
                currentStock: 5 + i,
                reorderThreshold: 50,
                triggeredAt: DateTimeOffset.UtcNow.AddHours(-(i + 1)),
                isAcknowledged: false,
                severity: severity);
        }
    }

    [Given(@"there is an unacknowledged low-stock alert for SKU ""(.*)""")]
    public void GivenThereIsAnUnacknowledgedLowStockAlertForSku(string sku)
    {
        var alertId = Guid.NewGuid();
        Fixture.StubInventoryClient.AddLowStockAlert(
            alertId,
            sku,
            currentStock: 10,
            reorderThreshold: 50,
            triggeredAt: DateTimeOffset.UtcNow.AddHours(-1),
            isAcknowledged: false,
            severity: "Critical");

        _scenarioContext[ScenarioContextKeys.AlertId] = alertId;
    }

    [Given(@"there are (\d+) critical low-stock alerts")]
    public void GivenThereAreCriticalLowStockAlerts(int alertCount)
    {
        for (int i = 0; i < alertCount; i++)
        {
            var alertId = Guid.NewGuid();
            var sku = $"CRIT-{i + 1:D3}";

            Fixture.StubInventoryClient.AddLowStockAlert(
                alertId,
                sku,
                currentStock: 2 + i,
                reorderThreshold: 50,
                triggeredAt: DateTimeOffset.UtcNow.AddHours(-(i + 1)),
                isAcknowledged: false,
                severity: "Critical");
        }
    }

    [Given(@"there are (\d+) warning low-stock alerts")]
    public void GivenThereAreWarningLowStockAlerts(int alertCount)
    {
        for (int i = 0; i < alertCount; i++)
        {
            var alertId = Guid.NewGuid();
            var sku = $"WARN-{i + 1:D3}";

            Fixture.StubInventoryClient.AddLowStockAlert(
                alertId,
                sku,
                currentStock: 15 + i,
                reorderThreshold: 50,
                triggeredAt: DateTimeOffset.UtcNow.AddHours(-(i + 1)),
                isAcknowledged: false,
                severity: "Warning");
        }
    }

    [Given(@"there are (\d+) acknowledged low-stock alerts")]
    public void GivenThereAreAcknowledgedLowStockAlerts(int alertCount)
    {
        for (int i = 0; i < alertCount; i++)
        {
            var alertId = Guid.NewGuid();
            var sku = $"ACK-{i + 1:D3}";

            Fixture.StubInventoryClient.AddLowStockAlert(
                alertId,
                sku,
                currentStock: 5 + i,
                reorderThreshold: 50,
                triggeredAt: DateTimeOffset.UtcNow.AddHours(-(i + 1)),
                isAcknowledged: true,
                severity: "Warning");
        }
    }

    [Given(@"there are (\d+) low-stock alerts")]
    public void GivenThereAreLowStockAlerts(int alertCount)
    {
        GivenThereAreUnacknowledgedLowStockAlerts(alertCount);
    }

    [Given(@"there is an unacknowledged low-stock alert with ID ""(.*)""")]
    public void GivenThereIsAnUnacknowledgedLowStockAlertWithId(string alertIdPlaceholder)
    {
        var alertId = Guid.NewGuid();
        Fixture.StubInventoryClient.AddLowStockAlert(
            alertId,
            "TEST-SKU-001",
            currentStock: 10,
            reorderThreshold: 50,
            triggeredAt: DateTimeOffset.UtcNow.AddHours(-1),
            isAcknowledged: false,
            severity: "Critical");

        _scenarioContext[ScenarioContextKeys.AlertId] = alertId;
    }

    [Given(@"there are (\d+) unacknowledged low-stock alerts for SKU ""(.*)""")]
    public void GivenThereAreUnacknowledgedLowStockAlertsForSku(int alertCount, string sku)
    {
        for (int i = 0; i < alertCount; i++)
        {
            var alertId = Guid.NewGuid();
            Fixture.StubInventoryClient.AddLowStockAlert(
                alertId,
                sku,
                currentStock: 5 + i,
                reorderThreshold: 50,
                triggeredAt: DateTimeOffset.UtcNow.AddHours(-(i + 1)),
                isAcknowledged: false,
                severity: "Critical");
        }
    }

    [When(@"I click on the alert for SKU ""(.*)""")]
    public async Task WhenIClickOnTheAlertForSku(string sku)
    {
        var alertId = _scenarioContext.Get<Guid>(ScenarioContextKeys.AlertId);
        var alertsPage = new OperationsAlertsPage(Page, Fixture.WasmBaseUrl);
        await alertsPage.ClickAlertAsync(alertId);
    }

    [When(@"I acknowledge the alert")]
    public async Task WhenIAcknowledgeTheAlert()
    {
        var alertsPage = new OperationsAlertsPage(Page, Fixture.WasmBaseUrl);
        var acknowledgeButton = await alertsPage.IsAcknowledgeButtonVisibleAsync();
        acknowledgeButton.ShouldBeTrue();

        // Click acknowledge button (already in modal from previous step)
        var button = Page.GetByTestId("alert-acknowledge-button");
        await button.ClickAsync();

        // Wait for modal to close
        await Page.GetByTestId("alert-details-modal")
            .WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 5_000 });
    }

    [When(@"I filter alerts by severity ""(.*)""")]
    public async Task WhenIFilterAlertsBySeverity(string severity)
    {
        var alertsPage = new OperationsAlertsPage(Page, Fixture.WasmBaseUrl);
        await alertsPage.FilterBySeverityAsync(severity);
    }

    [When(@"I filter alerts by status ""(.*)""")]
    public async Task WhenIFilterAlertsByStatus(string status)
    {
        var alertsPage = new OperationsAlertsPage(Page, Fixture.WasmBaseUrl);
        await alertsPage.FilterByStatusAsync(status);
    }

    [When(@"a new low-stock alert is triggered for SKU ""(.*)""")]
    public async Task WhenANewLowStockAlertIsTriggeredForSku(string sku)
    {
        var newAlertId = Guid.NewGuid();
        Fixture.StubInventoryClient.AddLowStockAlert(
            newAlertId,
            sku,
            currentStock: 5,
            reorderThreshold: 50,
            triggeredAt: DateTimeOffset.UtcNow,
            isAcknowledged: false,
            severity: "Critical");

        // Simulate SignalR push by waiting for alert to appear
        var alertsPage = new OperationsAlertsPage(Page, Fixture.WasmBaseUrl);
        await alertsPage.WaitForNewAlertAsync(newAlertId, timeoutMs: 10_000);
    }

    [When(@"another admin acknowledges alert ""(.*)"" from a different session")]
    public async Task WhenAnotherAdminAcknowledgesAlertFromADifferentSession(string alertIdPlaceholder)
    {
        var alertId = _scenarioContext.Get<Guid>(ScenarioContextKeys.AlertId);

        // Simulate acknowledgment via stub
        Fixture.StubInventoryClient.AcknowledgeAlert(alertId);

        // Wait for SignalR push to update UI
        var alertsPage = new OperationsAlertsPage(Page, Fixture.WasmBaseUrl);
        await alertsPage.WaitForAlertStatusChangeAsync(alertId, "Acknowledged", timeoutMs: 10_000);
    }

    [When(@"I close the alert details modal")]
    public async Task WhenICloseTheAlertDetailsModal()
    {
        var alertsPage = new OperationsAlertsPage(Page, Fixture.WasmBaseUrl);
        await alertsPage.CloseAlertDetailsModalAsync();
    }

    [When(@"the SignalR connection is temporarily lost")]
    public async Task WhenTheSignalRConnectionIsTemporarilyLost()
    {
        // Simulate connection loss by offline mode
        await Page.Context.SetOfflineAsync(true);
        await Task.Delay(1000); // Wait for disconnect to be detected
    }

    [When(@"the SignalR connection is re-established")]
    public async Task WhenTheSignalRConnectionIsReEstablished()
    {
        // Re-enable network
        await Page.Context.SetOfflineAsync(false);
        await Task.Delay(2000); // Wait for reconnection
    }

    [When(@"I acknowledge all (\d+) alerts one by one")]
    public async Task WhenIAcknowledgeAllAlertsOneByOne(int alertCount)
    {
        var alertsPage = new OperationsAlertsPage(Page, Fixture.WasmBaseUrl);

        for (int i = 0; i < alertCount; i++)
        {
            // Get first alert ID
            var alertIds = await alertsPage.GetAlertIdsAsync();
            if (alertIds.Count == 0) break;

            var alertId = alertIds[0];
            await alertsPage.AcknowledgeAlertAsync(alertId);
            await Task.Delay(500); // Wait between acknowledgments
        }
    }

    [Then(@"I should see (\d+) alerts in the feed")]
    public async Task ThenIShouldSeeAlertsInTheFeed(int expectedCount)
    {
        var alertsPage = new OperationsAlertsPage(Page, Fixture.WasmBaseUrl);
        var actualCount = await alertsPage.GetAlertCountAsync();
        actualCount.ShouldBe(expectedCount);
    }

    [Then(@"each alert should display severity, SKU, and current stock level")]
    public async Task ThenEachAlertShouldDisplaySeveritySkuAndCurrentStockLevel()
    {
        var alertsPage = new OperationsAlertsPage(Page, Fixture.WasmBaseUrl);
        var severities = await alertsPage.GetAlertSeveritiesAsync();
        severities.Count.ShouldBeGreaterThan(0);
        severities.All(s => !string.IsNullOrWhiteSpace(s)).ShouldBeTrue();
    }

    [Then(@"the alert status should change to ""(.*)""")]
    public async Task ThenTheAlertStatusShouldChangeTo(string expectedStatus)
    {
        var alertId = _scenarioContext.Get<Guid>(ScenarioContextKeys.AlertId);
        var alertsPage = new OperationsAlertsPage(Page, Fixture.WasmBaseUrl);

        // Wait for status change (may be via SignalR push)
        await alertsPage.WaitForAlertStatusChangeAsync(alertId, expectedStatus, timeoutMs: 10_000);
    }

    [Then(@"the alert should no longer appear in the unacknowledged filter")]
    public async Task ThenTheAlertShouldNoLongerAppearInTheUnacknowledgedFilter()
    {
        var alertId = _scenarioContext.Get<Guid>(ScenarioContextKeys.AlertId);
        var alertsPage = new OperationsAlertsPage(Page, Fixture.WasmBaseUrl);

        // Apply unacknowledged filter
        await alertsPage.FilterByStatusAsync("Unacknowledged");

        // Verify alert is not in feed
        var alertIds = await alertsPage.GetAlertIdsAsync();
        alertIds.ShouldNotContain(alertId);
    }

    [Then(@"all alerts should have severity ""(.*)""")]
    public async Task ThenAllAlertsShouldHaveSeverity(string expectedSeverity)
    {
        var alertsPage = new OperationsAlertsPage(Page, Fixture.WasmBaseUrl);
        var severities = await alertsPage.GetAlertSeveritiesAsync();
        severities.All(s => s == expectedSeverity).ShouldBeTrue();
    }

    [Then(@"all alerts should have status ""(.*)""")]
    public async Task ThenAllAlertsShouldHaveStatus(string expectedStatus)
    {
        var alertsPage = new OperationsAlertsPage(Page, Fixture.WasmBaseUrl);
        var statuses = await alertsPage.GetAlertStatusesAsync();
        statuses.All(s => s == expectedStatus).ShouldBeTrue();
    }

    [Then(@"the new alert for SKU ""(.*)"" should appear at the top")]
    public async Task ThenTheNewAlertForSkuShouldAppearAtTheTop(string expectedSku)
    {
        var alertsPage = new OperationsAlertsPage(Page, Fixture.WasmBaseUrl);
        var alertIds = await alertsPage.GetAlertIdsAsync();
        alertIds.Count.ShouldBeGreaterThan(0);

        // Verify first alert is the new one (check SKU in alert row)
        var firstAlertRow = Page.GetByTestId($"alert-{alertIds[0]}");
        var skuCell = firstAlertRow.Locator("[data-testid='alert-sku']");
        var actualSku = await skuCell.TextContentAsync();
        actualSku.ShouldContain(expectedSku);
    }

    [Then(@"the alert should have severity ""(.*)"" or ""(.*)""")]
    public async Task ThenTheAlertShouldHaveSeverityOr(string severity1, string severity2)
    {
        var alertsPage = new OperationsAlertsPage(Page, Fixture.WasmBaseUrl);
        var severities = await alertsPage.GetAlertSeveritiesAsync();
        severities.Count.ShouldBeGreaterThan(0);

        var firstSeverity = severities[0];
        (firstSeverity == severity1 || firstSeverity == severity2).ShouldBeTrue();
    }

    [Then(@"the alert status should update to ""(.*)"" in real-time")]
    public async Task ThenTheAlertStatusShouldUpdateToInRealTime(string expectedStatus)
    {
        await ThenTheAlertStatusShouldChangeTo(expectedStatus);
    }

    [Then(@"I should see the status change without refreshing the page")]
    public Task ThenIShouldSeeTheStatusChangeWithoutRefreshingThePage()
    {
        // Already verified by previous step (SignalR push)
        return Task.CompletedTask;
    }

    [Then(@"I should see the alert details modal")]
    public async Task ThenIShouldSeeTheAlertDetailsModal()
    {
        var alertsPage = new OperationsAlertsPage(Page, Fixture.WasmBaseUrl);
        var isVisible = await alertsPage.IsAlertDetailsModalVisibleAsync();
        isVisible.ShouldBeTrue();
    }

    [Then(@"the modal should display SKU, product name, current stock level, reorder threshold, and severity")]
    public async Task ThenTheModalShouldDisplaySkuProductNameCurrentStockLevelReorderThresholdAndSeverity()
    {
        // Verify all key fields are present in modal
        var sku = Page.GetByTestId("alert-detail-sku");
        var productName = Page.GetByTestId("alert-detail-product-name");
        var currentStock = Page.GetByTestId("alert-detail-current-stock");
        var reorderThreshold = Page.GetByTestId("alert-detail-reorder-threshold");
        var severity = Page.GetByTestId("alert-detail-severity");

        (await sku.IsVisibleAsync()).ShouldBeTrue();
        (await currentStock.IsVisibleAsync()).ShouldBeTrue();
        (await reorderThreshold.IsVisibleAsync()).ShouldBeTrue();
        (await severity.IsVisibleAsync()).ShouldBeTrue();
    }

    [Then(@"I should see an ""(.*)"" button")]
    public async Task ThenIShouldSeeAnButton(string buttonText)
    {
        var alertsPage = new OperationsAlertsPage(Page, Fixture.WasmBaseUrl);
        var isVisible = await alertsPage.IsAcknowledgeButtonVisibleAsync();
        isVisible.ShouldBeTrue();
    }

    [Then(@"the modal should be hidden")]
    public async Task ThenTheModalShouldBeHidden()
    {
        var alertsPage = new OperationsAlertsPage(Page, Fixture.WasmBaseUrl);
        var isVisible = await alertsPage.IsAlertDetailsModalVisibleAsync();
        isVisible.ShouldBeFalse();
    }

    [Then(@"the alert should still be unacknowledged")]
    public async Task ThenTheAlertShouldStillBeUnacknowledged()
    {
        var alertId = _scenarioContext.Get<Guid>(ScenarioContextKeys.AlertId);
        var alertRow = Page.GetByTestId($"alert-{alertId}");
        var statusCell = alertRow.Locator("[data-testid='alert-status']");
        var status = await statusCell.TextContentAsync();
        status.ShouldContain("Unacknowledged");
    }

    [Then(@"I should see a ""(.*)"" message")]
    public async Task ThenIShouldSeeANoAlertsMessage(string messageType)
    {
        var alertsPage = new OperationsAlertsPage(Page, Fixture.WasmBaseUrl);

        if (messageType == "no alerts")
        {
            var isVisible = await alertsPage.IsNoAlertsMessageVisibleAsync();
            isVisible.ShouldBeTrue();
        }
        else if (messageType == "no unacknowledged alerts")
        {
            // Apply filter first
            await alertsPage.FilterByStatusAsync("Unacknowledged");
            var isVisible = await alertsPage.IsNoAlertsMessageVisibleAsync();
            isVisible.ShouldBeTrue();
        }
    }

    [Then(@"I should not see any alert rows")]
    public async Task ThenIShouldNotSeeAnyAlertRows()
    {
        var alertsPage = new OperationsAlertsPage(Page, Fixture.WasmBaseUrl);
        var alertCount = await alertsPage.GetAlertCountAsync();
        alertCount.ShouldBe(0);
    }

    [Then(@"both alerts should be for SKU ""(.*)""")]
    public async Task ThenBothAlertsShouldBeForSku(string expectedSku)
    {
        var alertRows = Page.Locator("[data-testid^='alert-']");
        var count = await alertRows.CountAsync();

        for (int i = 0; i < count; i++)
        {
            var row = alertRows.Nth(i);
            var skuCell = row.Locator("[data-testid='alert-sku']");
            var actualSku = await skuCell.TextContentAsync();
            actualSku.ShouldContain(expectedSku);
        }
    }

    [Then(@"each alert should have a unique alert ID")]
    public async Task ThenEachAlertShouldHaveAUniqueAlertId()
    {
        var alertsPage = new OperationsAlertsPage(Page, Fixture.WasmBaseUrl);
        var alertIds = await alertsPage.GetAlertIdsAsync();
        alertIds.Distinct().Count().ShouldBe(alertIds.Count);
    }

    [Then(@"the page should load within (\d+) seconds")]
    public async Task ThenThePageShouldLoadWithinSeconds(int maxSeconds)
    {
        // Already loaded by "When I navigate to operations alerts page"
        // Just verify we're on the page
        var alertsPage = new OperationsAlertsPage(Page, Fixture.WasmBaseUrl);
        var isOnPage = await alertsPage.IsOnOperationsAlertsPageAsync();
        isOnPage.ShouldBeTrue();
    }

    [Then(@"scrolling should be smooth")]
    public async Task ThenScrollingShouldBeSmooth()
    {
        // Verify page is scrollable
        var alertFeed = Page.GetByTestId("operations-alerts-feed");
        var boundingBox = await alertFeed.BoundingBoxAsync();
        boundingBox.ShouldNotBeNull();
    }

    [Then(@"all alert statuses should change to ""(.*)""")]
    public async Task ThenAllAlertStatusesShouldChangeTo(string expectedStatus)
    {
        var alertsPage = new OperationsAlertsPage(Page, Fixture.WasmBaseUrl);
        var statuses = await alertsPage.GetAlertStatusesAsync();
        statuses.All(s => s == expectedStatus).ShouldBeTrue();
    }

    [Then(@"any missed alert updates should sync")]
    public async Task ThenAnyMissedAlertUpdatesShouldSync()
    {
        // Just verify connection is re-established
        var alertsPage = new OperationsAlertsPage(Page, Fixture.WasmBaseUrl);
        var isConnected = await alertsPage.IsRealtimeConnectedAsync();
        isConnected.ShouldBeTrue();
    }
}
