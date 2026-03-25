using Microsoft.Playwright;

namespace Backoffice.E2ETests.Pages;

/// <summary>
/// Page Object Model for the Backoffice Return Management page.
/// Covers return queue display, status filtering, and navigation.
/// Implements patterns from M32/M33 E2E sessions (WASM navigation, MudBlazor interactions, test-id conventions).
/// </summary>
public sealed class ReturnManagementPage
{
    private readonly IPage _page;
    private readonly string _baseUrl;

    // Timeout constants for WASM hydration and MudBlazor interactions
    private const int WasmHydrationTimeoutMs = 30_000; // WASM bootstrap + MudBlazor provider
    private const int MudSelectListboxTimeoutMs = 15_000; // MudSelect popover open + animation
    private const int ApiCallTimeoutMs = 15_000; // Network call + response processing

    public ReturnManagementPage(IPage page, string baseUrl)
    {
        _page = page;
        _baseUrl = baseUrl;
    }

    // Locators - Page Structure
    private ILocator PageHeading => _page.GetByTestId("page-heading").Or(_page.Locator("h4:has-text('Return Management')"));
    private ILocator StatusFilter => _page.GetByTestId("status-filter");
    private ILocator LoadReturnsButton => _page.GetByTestId("load-returns-btn");
    private ILocator RefreshReturnsButton => _page.GetByTestId("refresh-returns-btn");
    private ILocator LoadingIndicator => _page.GetByTestId("returns-loading");
    private ILocator NoReturnsAlert => _page.GetByTestId("no-returns-alert");
    private ILocator ReturnsTable => _page.GetByTestId("returns-table");
    private ILocator ReturnCountBadge => _page.GetByTestId("return-count-badge");

    // Locators - Navigation
    private ILocator ReturnManagementNavLink => _page.GetByTestId("nav-return-management")
        .Or(_page.Locator("a[href='/returns']"));

    // Actions - Navigation
    public async Task NavigateAsync()
    {
        await _page.GotoAsync($"{_baseUrl}/returns");
        await WaitForPageLoadedAsync();
    }

    public async Task NavigateFromDashboardAsync()
    {
        await ReturnManagementNavLink.ClickAsync();

        // Wait for WASM client-side navigation to complete
        // Blazor WASM routing doesn't trigger full page load events
        await _page.WaitForURLAsync(
            url => url.Contains("/returns"),
            new() { Timeout = 5_000 });

        await WaitForPageLoadedAsync();
    }

    public async Task WaitForPageLoadedAsync()
    {
        // Wait for MudBlazor framework to hydrate (WASM pattern)
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Wait for MudBlazor provider (CSS framework initialization)
        await _page.WaitForSelectorAsync(".mud-dialog-provider",
            new() { State = WaitForSelectorState.Attached, Timeout = WasmHydrationTimeoutMs });

        // Wait for Return Management page-specific elements
        // Either the status filter (success) OR an error message (failure) should appear
        try
        {
            await _page.WaitForSelectorAsync(
                "[data-testid='status-filter'], [data-testid='authorization-error'], [data-testid='session-expired']",
                new() { Timeout = ApiCallTimeoutMs });
        }
        catch (TimeoutException)
        {
            // Page may have loaded but no returns data yet — this is acceptable
            // Assertions in step definitions will catch actual failures
        }
    }

    // Actions - Filtering
    public async Task SelectStatusFilterAsync(string status)
    {
        // Wait for the status filter dropdown to be visible
        await StatusFilter.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = ApiCallTimeoutMs });

        // Force-click inner trigger (MudBlazor pattern from M32 E2E sessions)
        // MudBlazor's transparent .mud-input-mask blocks normal click hit-test
        await StatusFilter.Locator(".mud-select-input")
            .ClickAsync(new LocatorClickOptions { Force = true });

        // Wait for MudBlazor listbox portal to render (at document.body level, not inside StatusFilter)
        await _page.WaitForSelectorAsync("[role='listbox']",
            new() { Timeout = MudSelectListboxTimeoutMs });

        // Click the option by value text
        // MudBlazor renders options with text content matching the SelectItem value
        var optionLocator = _page.Locator($"[role='option']:has-text('{status}')");
        await optionLocator.ClickAsync(new LocatorClickOptions { Timeout = 10_000 });
    }

    public async Task ClickLoadReturnsButtonAsync()
    {
        await LoadReturnsButton.ClickAsync();

        // Wait for loading indicator to appear and disappear
        try
        {
            await LoadingIndicator.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 2_000 });
            await LoadingIndicator.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = ApiCallTimeoutMs });
        }
        catch (TimeoutException)
        {
            // Loading may have been too fast to observe, or API call failed — acceptable
            // Assertions in step definitions will verify the actual outcome
        }
    }

    public async Task ClickRefreshReturnsButtonAsync()
    {
        await RefreshReturnsButton.ClickAsync();

        // Wait for API call to complete (similar to LoadReturnsButton)
        try
        {
            await LoadingIndicator.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 2_000 });
            await LoadingIndicator.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = ApiCallTimeoutMs });
        }
        catch (TimeoutException)
        {
            // Acceptable — assertions will verify outcome
        }
    }

    // Assertions - Page State
    public async Task<bool> IsOnReturnManagementPageAsync()
    {
        return _page.Url.Contains("/returns");
    }

    public async Task<bool> IsPageHeadingVisibleAsync()
    {
        try
        {
            await PageHeading.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    public async Task<string?> GetPageHeadingTextAsync()
    {
        return await PageHeading.TextContentAsync();
    }

    // Assertions - Filter State
    public async Task<string?> GetSelectedStatusFilterValueAsync()
    {
        // MudSelect stores selected value in the input's value attribute
        var inputLocator = StatusFilter.Locator("input");
        return await inputLocator.InputValueAsync();
    }

    // Assertions - Returns List
    public async Task<int> GetReturnCountAsync()
    {
        // Check if "no returns" alert is showing first
        var isNoReturnsVisible = await IsNoReturnsAlertVisibleAsync();
        if (isNoReturnsVisible)
        {
            return 0;
        }

        // Count table rows (excluding header row)
        try
        {
            var rows = await ReturnsTable.Locator("tbody tr[data-testid^='return-row-']").AllAsync();
            return rows.Count;
        }
        catch
        {
            // Table may not exist yet
            return 0;
        }
    }

    public async Task<bool> IsNoReturnsAlertVisibleAsync()
    {
        try
        {
            await NoReturnsAlert.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 2_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    public async Task<bool> IsReturnsTableVisibleAsync()
    {
        try
        {
            await ReturnsTable.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 2_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    public async Task<bool> IsReturnCountBadgeVisibleAsync()
    {
        try
        {
            await ReturnCountBadge.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 2_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    public async Task<string?> GetReturnCountBadgeTextAsync()
    {
        if (await IsReturnCountBadgeVisibleAsync())
        {
            return await ReturnCountBadge.TextContentAsync();
        }
        return null;
    }

    public async Task<IReadOnlyList<string>> GetDisplayedReturnStatusesAsync()
    {
        var statuses = new List<string>();
        var rows = await ReturnsTable.Locator("tbody tr[data-testid^='return-row-']").AllAsync();

        foreach (var row in rows)
        {
            var statusCell = row.Locator("[data-testid='return-status']");
            var statusText = await statusCell.TextContentAsync();
            if (!string.IsNullOrWhiteSpace(statusText))
            {
                statuses.Add(statusText.Trim());
            }
        }

        return statuses;
    }

    // Assertions - Authorization
    public async Task<bool> IsAuthorizationErrorVisibleAsync()
    {
        try
        {
            var authErrorLocator = _page.GetByTestId("authorization-error")
                .Or(_page.Locator("text=You do not have permission"))
                .Or(_page.Locator("text=Unauthorized"));

            await authErrorLocator.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 2_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    // Assertions - Session Expiry
    public async Task<bool> IsSessionExpiredModalVisibleAsync()
    {
        try
        {
            var sessionExpiredModal = _page.GetByTestId("session-expired-modal")
                .Or(_page.Locator("text=Your session has expired"));

            await sessionExpiredModal.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 2_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }
}
