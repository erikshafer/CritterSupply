namespace VendorPortal.E2ETests.Pages;

/// <summary>
/// Page Object Model for the Vendor Portal dashboard page (/dashboard).
/// Uses data-testid attributes for stable element selection.
/// </summary>
public sealed class VendorDashboardPage(IPage page)
{
    public async Task NavigateAsync() => await page.GotoAsync("/dashboard");

    // ─── KPI Cards ───

    public ILocator LowStockAlertsCard => page.GetByTestId("kpi-low-stock-alerts");
    public ILocator PendingChangeRequestsCard => page.GetByTestId("kpi-pending-change-requests");
    public ILocator TotalSkusCard => page.GetByTestId("kpi-total-skus");

    public async Task<string> GetLowStockAlertsCountAsync() =>
        await LowStockAlertsCard.Locator("h4").InnerTextAsync();

    public async Task<string> GetPendingChangeRequestsCountAsync() =>
        await PendingChangeRequestsCard.Locator("h4").InnerTextAsync();

    public async Task<string> GetTotalSkusCountAsync() =>
        await TotalSkusCard.Locator("h4").InnerTextAsync();

    // ─── Hub Status ───

    public ILocator HubStatusIndicator => page.GetByTestId("hub-status-indicator");
    public ILocator HubDisconnectedBanner => page.GetByTestId("hub-disconnected-banner");

    // ─── Quick Actions ───

    public ILocator SubmitChangeRequestButton => page.GetByTestId("submit-change-request-btn");
    public ILocator ViewChangeRequestsButton => page.GetByTestId("view-change-requests-btn");
    public ILocator SettingsButton => page.GetByTestId("settings-btn");

    // ─── Banners ───

    public ILocator SalesMetricUpdatedBanner => page.GetByTestId("sales-metric-updated-banner");
}
