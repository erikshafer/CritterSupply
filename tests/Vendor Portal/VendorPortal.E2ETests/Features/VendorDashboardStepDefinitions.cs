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

        // Wait for WASM hydration and login form to be ready
        // CI environments require 60s timeout due to slower network and cold WASM runtime
        await Page.WaitForSelectorAsync("[data-testid='login-btn']", new PageWaitForSelectorOptions
        {
            Timeout = 60000 // WASM cold start can take up to 60s in CI (30-40s observed)
        });

        // Fill credentials and submit (without re-navigating)
        await loginPage.FillEmailAsync(email);
        await loginPage.FillPasswordAsync(password);
        await loginPage.ClickSignInAsync();

        // CRITICAL: Wait for BOTH URL change AND dashboard element to be visible
        // CI environments need more time than local development (network latency, container startup)
        // Increased timeout from 15s → 30s for CI stability

        // Step 1: Wait for URL to change to /dashboard (Blazor client-side routing)
        await Page.WaitForURLAsync("**/dashboard", new PageWaitForURLOptions
        {
            Timeout = 30000,
            WaitUntil = WaitUntilState.Commit
        });

        // Step 2: Wait for a dashboard element to be visible (ensures WASM components are rendered)
        // Use KPI card as smoke test — if this is visible, dashboard is fully loaded
        await Page.WaitForSelectorAsync("[data-testid='kpi-low-stock-alerts']", new PageWaitForSelectorOptions
        {
            Timeout = 30000,
            State = WaitForSelectorState.Visible
        });

        // Step 3: Wait for SignalR hub connection to establish before navigating away from dashboard
        // Dashboard.OnInitializedAsync() calls HubService.ConnectAsync() asynchronously.
        // The hub status indicator aria-label changes to "Live" once connected.
        // Without this wait, tests that immediately navigate to other pages encounter unhandled
        // exceptions during hub connection, causing Blazor error UI to appear and block interactions.
        // Use a generous timeout since test infrastructure (TestContainers + WASM) is slower than production.

        // PATTERN CHANGE (M34.0): Remove aggressive error UI check that created false positives.
        // The previous pattern (wait 5s + check error UI) had a race condition where the hub
        // could connect successfully but then disconnect immediately after, causing tests to fail
        // even though the login flow itself worked correctly.
        //
        // NEW PATTERN: Simply verify hub indicator appears and shows "Live" state within timeout.
        // If tests need the hub to be connected for SignalR interactions, they should explicitly
        // wait for hub state in their own Given/When steps, not in the login precondition.
        var hubIndicator = Page.GetByTestId("hub-status-indicator");
        await hubIndicator.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });

        // Poll until aria-label is "Live" (hub is connected) with more generous timeout for CI
        var maxAttempts = 60; // 60 attempts * 500ms = 30 seconds max
        for (int i = 0; i < maxAttempts; i++)
        {
            var ariaLabel = await hubIndicator.GetAttributeAsync("aria-label");
            if (ariaLabel == "Live")
            {
                // Hub reported Live — good enough for login flow completion
                // If subsequent test steps depend on hub staying connected, they should
                // verify hub state themselves
                return;
            }

            if (i == maxAttempts - 1)
            {
                // Hub never reached Live state — this IS a real failure
                throw new TimeoutException($"Hub connection did not reach 'Live' state within {maxAttempts * 500}ms. Last state: {ariaLabel}");
            }

            await Page.WaitForTimeoutAsync(500);
        }
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

        var tenantId = WellKnownVendorTestData.Tenant.HearthHoundTenantId;

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
        var tenantId = WellKnownVendorTestData.Tenant.HearthHoundTenantId;

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
        // IMPORTANT: Multiple snackbars can be present (e.g., "Welcome back" from login + new message)
        // We need to find the snackbar that contains our expected text
        var snackbar = Page.Locator(".mud-snackbar");

        // Wait for at least one snackbar to be visible
        await snackbar.First.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });

        // Check all visible snackbars until we find one with the expected text
        var count = await snackbar.CountAsync();
        var found = false;

        for (int i = 0; i < count; i++)
        {
            try
            {
                var text = await snackbar.Nth(i).InnerTextAsync();
                if (text.ToLowerInvariant().Contains(expectedText.ToLowerInvariant()))
                {
                    found = true;
                    break;
                }
            }
            catch
            {
                // Snackbar may have disappeared, continue to next
                continue;
            }
        }

        found.ShouldBeTrue($"Expected to find snackbar containing '{expectedText}' but checked {count} snackbars and didn't find it");
    }
}
