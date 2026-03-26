using Microsoft.Playwright;

namespace Backoffice.E2ETests.Pages;

/// <summary>
/// Page Object Model for the Backoffice Executive Dashboard.
/// Covers KPI cards, real-time updates via SignalR, and navigation to detail views.
/// </summary>
public sealed class DashboardPage
{
    private readonly IPage _page;
    private readonly string _baseUrl;

    public DashboardPage(IPage page, string baseUrl)
    {
        _page = page;
        _baseUrl = baseUrl;
    }

    // Locators - KPI Cards
    private ILocator TotalOrdersCard => _page.GetByTestId("kpi-total-orders");
    private ILocator RevenueCard => _page.GetByTestId("kpi-revenue");
    private ILocator PendingReturnsCard => _page.GetByTestId("kpi-pending-returns");
    private ILocator LowStockAlertsCard => _page.GetByTestId("kpi-low-stock-alerts");
    // Note: Active Customers KPI removed - not in M32.1 scope (deferred to Phase 3+)

    // Locators - Data Freshness (P1-2)
    private ILocator MetricsUpdatedBanner => _page.GetByTestId("metrics-updated-banner");
    private ILocator DashboardRefreshButton => _page.GetByTestId("dashboard-refresh-btn");

    // Locators - Navigation
    private ILocator CustomerServiceLink => _page.GetByTestId("nav-customer-service");
    private ILocator OperationsLink => _page.GetByTestId("nav-operations");
    private ILocator AnalyticsLink => _page.GetByTestId("nav-analytics");

    // Locators - Real-time indicator
    private ILocator RealtimeIndicator => _page.GetByTestId("realtime-connected");
    private ILocator RealtimeDisconnected => _page.GetByTestId("realtime-disconnected");

    // Actions
    public async Task NavigateAsync()
    {
        await _page.GotoAsync($"{_baseUrl}/dashboard", new PageGotoOptions { WaitUntil = WaitUntilState.Commit });

        // Wait for dashboard to be fully loaded (MudBlazor hydration + KPI cards rendered)
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await TotalOrdersCard.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
    }

    public async Task WaitForRealtimeConnectionAsync()
    {
        // Wait for SignalR connection indicator to show "Connected"
        // SignalR connection may take longer after JWT auth completes
        await RealtimeIndicator.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
    }

    public async Task NavigateToCustomerServiceAsync()
    {
        await CustomerServiceLink.ClickAsync();
        await _page.WaitForURLAsync(url => url.Contains("/customers/search"), new() { Timeout = 5_000 });
    }

    public async Task NavigateToOperationsAsync()
    {
        await OperationsLink.ClickAsync();
        await _page.WaitForURLAsync(url => url.Contains("/operations"), new() { Timeout = 5_000 });
    }

    public async Task NavigateToAnalyticsAsync()
    {
        await AnalyticsLink.ClickAsync();
        await _page.WaitForURLAsync(url => url.Contains("/analytics"), new() { Timeout = 5_000 });
    }

    // Assertions - KPI Values
    public async Task<string?> GetTotalOrdersValueAsync()
    {
        var valueLocator = TotalOrdersCard.Locator("[data-testid='kpi-value']");
        return await valueLocator.TextContentAsync();
    }

    public async Task<string?> GetRevenueValueAsync()
    {
        var valueLocator = RevenueCard.Locator("[data-testid='kpi-value']");
        return await valueLocator.TextContentAsync();
    }

    public async Task<string?> GetPendingReturnsValueAsync()
    {
        var valueLocator = PendingReturnsCard.Locator("[data-testid='kpi-value']");
        return await valueLocator.TextContentAsync();
    }

    public async Task<string?> GetLowStockAlertsValueAsync()
    {
        var valueLocator = LowStockAlertsCard.Locator("[data-testid='kpi-value']");
        return await valueLocator.TextContentAsync();
    }

    // Note: GetActiveCustomersValueAsync removed - Active Customers KPI not in M32.1 scope

    // Assertions - Real-time
    public async Task<bool> IsRealtimeConnectedAsync()
    {
        try
        {
            await RealtimeIndicator.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 2_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    public async Task<bool> IsRealtimeDisconnectedAsync()
    {
        try
        {
            await RealtimeDisconnected.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 2_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    public async Task<bool> IsOnDashboardPageAsync()
    {
        return _page.Url.Contains("/dashboard");
    }

    // Wait for KPI update (for real-time SignalR push scenarios)
    public async Task WaitForKpiUpdateAsync(string kpiTestId, string expectedValue, int timeoutMs = 5_000)
    {
        var kpiCard = _page.GetByTestId(kpiTestId);
        var valueLocator = kpiCard.Locator("[data-testid='kpi-value']");

        await valueLocator.WaitForAsync(new() { State = WaitForSelectorState.Visible });

        // Poll until value matches (SignalR push may take 100-500ms)
        var endTime = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < endTime)
        {
            var currentValue = await valueLocator.TextContentAsync();
            if (currentValue?.Trim() == expectedValue)
            {
                return;
            }
            await Task.Delay(100);
        }

        throw new TimeoutException($"KPI '{kpiTestId}' did not update to '{expectedValue}' within {timeoutMs}ms");
    }

    // P1-2: Data Freshness Indicators
    public async Task<bool> IsMetricsUpdatedBannerVisibleAsync()
    {
        try
        {
            await MetricsUpdatedBanner.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 2_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    public async Task<string?> GetMetricsUpdatedBannerTextAsync()
    {
        if (await IsMetricsUpdatedBannerVisibleAsync())
        {
            return await MetricsUpdatedBanner.TextContentAsync();
        }
        return null;
    }

    public async Task ClickRefreshButtonInBannerAsync()
    {
        // The banner has a "Refresh" button inside it
        var refreshButtonInBanner = MetricsUpdatedBanner.Locator("button:has-text('Refresh')");
        await refreshButtonInBanner.ClickAsync();
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    public async Task ClickDashboardRefreshButtonAsync()
    {
        await DashboardRefreshButton.ClickAsync();
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }
}
