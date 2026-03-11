namespace VendorPortal.E2ETests.Pages;

/// <summary>
/// Page Object Model for the settings page (/settings).
/// </summary>
public sealed class SettingsPage(IPage page)
{
    public async Task NavigateAsync() => await page.GotoAsync("/settings");

    public ILocator LowStockAlertsSwitch => page.GetByTestId("pref-low-stock-alerts");
    public ILocator ChangeRequestDecisionsSwitch => page.GetByTestId("pref-change-request-decisions");
    public ILocator InventoryUpdatesSwitch => page.GetByTestId("pref-inventory-updates");
    public ILocator SalesMetricsSwitch => page.GetByTestId("pref-sales-metrics");
    public ILocator SavePreferencesButton => page.GetByTestId("save-preferences-btn");

    public ILocator NoSavedViewsMessage => page.GetByTestId("no-saved-views-message");
    public ILocator SavedViewsTable => page.GetByTestId("saved-views-table");
}
