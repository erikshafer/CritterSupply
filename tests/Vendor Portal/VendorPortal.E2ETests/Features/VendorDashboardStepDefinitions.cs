using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using VendorPortal.E2ETests.Pages;

namespace VendorPortal.E2ETests.Features;

/// <summary>
/// Step definitions for vendor portal dashboard scenarios.
/// Covers P0: KPI cards, SignalR hub status, live updates, decision toasts.
/// </summary>
[Binding]
public sealed class VendorDashboardStepDefinitions
{
    private readonly ScenarioContext _scenarioContext;

    public VendorDashboardStepDefinitions(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
    }

    private IPage Page => _scenarioContext.Get<IPage>(ScenarioContextKeys.Page);
    private E2ETestFixture Fixture => _scenarioContext.Get<E2ETestFixture>(ScenarioContextKeys.Fixture);

    // ─── Given: Login ───

    [Given("I am logged in as {string} with password {string}")]
    public async Task GivenIAmLoggedInAs(string email, string password)
    {
        var loginPage = new VendorLoginPage(Page);
        await loginPage.NavigateAsync();

        // Wait for WASM hydration
        await Page.WaitForSelectorAsync("[data-testid='login-btn']", new PageWaitForSelectorOptions
        {
            Timeout = 30000
        });

        await loginPage.LoginAsync(email, password);

        // Wait for dashboard redirect
        await Page.WaitForURLAsync("**/dashboard", new PageWaitForURLOptions { Timeout = 15000 });
    }

    [Given("I am on the dashboard")]
    public async Task GivenIAmOnTheDashboard()
    {
        // Allow time for SignalR hub connection to establish after dashboard loads
        await Page.WaitForTimeoutAsync(2000);
    }

    // ─── Then: KPI Cards ───

    [Then("I should see the dashboard KPI cards")]
    public async Task ThenIShouldSeeTheDashboardKPICards()
    {
        var dashboard = new VendorDashboardPage(Page);
        await dashboard.LowStockAlertsCard.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });
        (await dashboard.LowStockAlertsCard.IsVisibleAsync()).ShouldBeTrue();
        (await dashboard.PendingChangeRequestsCard.IsVisibleAsync()).ShouldBeTrue();
        (await dashboard.TotalSkusCard.IsVisibleAsync()).ShouldBeTrue();
    }

    [Then("the low stock alerts count should be {string}")]
    public async Task ThenTheLowStockAlertsCountShouldBe(string expectedCount)
    {
        var dashboard = new VendorDashboardPage(Page);
        // Wait for the count to update (SignalR messages need processing time)
        await Page.WaitForTimeoutAsync(1000);
        var count = await dashboard.GetLowStockAlertsCountAsync();
        count.Trim().ShouldBe(expectedCount);
    }

    [Then("the pending change requests count should be {string}")]
    public async Task ThenThePendingChangeRequestsCountShouldBe(string expectedCount)
    {
        var dashboard = new VendorDashboardPage(Page);
        var count = await dashboard.GetPendingChangeRequestsCountAsync();
        count.Trim().ShouldBe(expectedCount);
    }

    [Then("the total SKUs count should be {string}")]
    public async Task ThenTheTotalSKUsCountShouldBe(string expectedCount)
    {
        var dashboard = new VendorDashboardPage(Page);
        var count = await dashboard.GetTotalSkusCountAsync();
        count.Trim().ShouldBe(expectedCount);
    }

    // ─── Then: Hub Status ───

    [Then("the hub status indicator should show {string}")]
    public async Task ThenTheHubStatusIndicatorShouldShow(string expectedStatus)
    {
        var dashboard = new VendorDashboardPage(Page);
        // Hub connection can take a moment after page load
        await Page.WaitForTimeoutAsync(3000);

        var indicator = dashboard.HubStatusIndicator;
        await indicator.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });
        var ariaLabel = await indicator.GetAttributeAsync("aria-label");
        ariaLabel.ShouldNotBeNull();
        ariaLabel.ShouldBe(expectedStatus);
    }

    // ─── When: SignalR Messages ───

    [When("a LowStockAlertRaised hub message is sent to the tenant group")]
    public async Task WhenALowStockAlertRaisedHubMessageIsSent()
    {
        var hubContext = Fixture.PortalApiHost.Services
            .GetRequiredService<IHubContext<VendorPortal.Api.Hubs.VendorPortalHub>>();

        var tenantId = WellKnownVendorTestData.Tenant.AcmeTenantId;

        // Wolverine wraps all messages in a CloudEvents envelope with "type" and "data" fields
        var envelope = new
        {
            type = "LowStockAlertRaised",
            data = new { sku = "DOG-BOWL-01", warehouseId = "WH-01", currentQuantity = 3, thresholdQuantity = 10 }
        };

        await hubContext.Clients
            .Group($"vendor:{tenantId}")
            .SendAsync("ReceiveMessage", JsonSerializer.SerializeToElement(envelope));

        // Allow time for the browser to process the hub message and update DOM
        await Page.WaitForTimeoutAsync(2000);
    }

    [When("a ChangeRequestDecisionPersonal hub message with decision {string} is sent")]
    public async Task WhenAChangeRequestDecisionPersonalHubMessageIsSent(string decision)
    {
        var hubContext = Fixture.PortalApiHost.Services
            .GetRequiredService<IHubContext<VendorPortal.Api.Hubs.VendorPortalHub>>();

        // Personal messages are sent to user:{userId} group
        // We use the admin user's ID from the seed data
        // Since we don't have the exact userId, send to the tenant group as a fallback
        var tenantId = WellKnownVendorTestData.Tenant.AcmeTenantId;

        var envelope = new
        {
            type = "ChangeRequestDecisionPersonal",
            data = new
            {
                decision,
                sku = "DOG-BOWL-01",
                requestId = Guid.NewGuid(),
                reason = decision == "Rejected" ? "Does not meet quality standards" : (string?)null,
                decidedAt = DateTimeOffset.UtcNow
            }
        };

        await hubContext.Clients
            .Group($"vendor:{tenantId}")
            .SendAsync("ReceiveMessage", JsonSerializer.SerializeToElement(envelope));

        // Allow time for the snackbar to appear
        await Page.WaitForTimeoutAsync(2000);
    }

    [Then("I should see a snackbar containing {string}")]
    public async Task ThenIShouldSeeASnackbarContaining(string expectedText)
    {
        // MudBlazor snackbar renders inside .mud-snackbar
        var snackbar = Page.Locator(".mud-snackbar");
        await snackbar.First.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });
        var text = await snackbar.First.InnerTextAsync();
        text.ToLowerInvariant().ShouldContain(expectedText.ToLowerInvariant());
    }
}
