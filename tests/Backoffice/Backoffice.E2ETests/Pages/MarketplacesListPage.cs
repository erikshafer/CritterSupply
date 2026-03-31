namespace Backoffice.E2ETests.Pages;

/// <summary>
/// Page Object Model for the Backoffice Marketplaces List page.
/// Route: /marketplaces
/// Covers marketplace table display, row data assertions, and status chip validation.
/// </summary>
public sealed class MarketplacesListPage
{
    private readonly IPage _page;
    private readonly string _baseUrl;

    private const int WasmBootstrapTimeoutMs = 60_000;
    private const int ApiCallTimeoutMs = 15_000;

    public MarketplacesListPage(IPage page, string baseUrl)
    {
        _page = page;
        _baseUrl = baseUrl;
    }

    // Locators — match MarketplacesList.razor data-testid attributes
    private ILocator PageContainer => _page.GetByTestId("marketplaces-page");
    private ILocator MarketplacesTable => _page.GetByTestId("marketplaces-table");
    private ILocator Breadcrumbs => _page.GetByTestId("marketplaces-breadcrumbs");
    private ILocator Title => _page.GetByTestId("marketplaces-title");
    private ILocator ErrorAlert => _page.GetByTestId("marketplaces-error");
    private ILocator Loading => _page.GetByTestId("marketplaces-loading");

    // ─── Navigation ────────────────────────────────────────────────────────

    public async Task NavigateAsync()
    {
        await _page.GotoAsync($"{_baseUrl}/marketplaces", new PageGotoOptions { WaitUntil = WaitUntilState.Commit });
        await WaitForLoadedAsync();
    }

    public async Task WaitForLoadedAsync()
    {
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Wait for MudBlazor framework to hydrate (WASM pattern)
        await _page.WaitForSelectorAsync(".mud-dialog-provider",
            new() { State = WaitForSelectorState.Attached, Timeout = WasmBootstrapTimeoutMs });

        // Wait for the marketplaces table to appear
        try
        {
            await MarketplacesTable.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = ApiCallTimeoutMs });
        }
        catch (TimeoutException)
        {
            // Table may not appear if no marketplaces exist — acceptable
        }
    }

    // ─── Table Assertions ──────────────────────────────────────────────────

    public async Task<int> GetRowCountAsync()
    {
        try
        {
            await MarketplacesTable.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
            var rows = await MarketplacesTable.Locator("tbody tr").AllAsync();
            // Filter out the "no records" row if present
            var dataRows = new List<ILocator>();
            foreach (var row in rows)
            {
                var testId = await row.Locator("td[data-testid^='marketplace-row-']").CountAsync();
                if (testId > 0) dataRows.Add(row);
            }
            return dataRows.Count;
        }
        catch (TimeoutException)
        {
            return 0;
        }
    }

    public async Task<bool> IsRowVisibleAsync(string channelCode)
    {
        var row = _page.GetByTestId($"marketplace-row-{channelCode}");
        try
        {
            await row.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    public async Task<string?> GetDisplayNameAsync(string channelCode)
    {
        var cell = _page.GetByTestId($"marketplace-display-name-{channelCode}");
        try
        {
            await cell.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
            return await cell.TextContentAsync();
        }
        catch (TimeoutException)
        {
            return null;
        }
    }

    public async Task<string?> GetStatusChipTextAsync(string channelCode)
    {
        var statusCell = _page.GetByTestId($"marketplace-status-{channelCode}");
        try
        {
            await statusCell.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
            // Find the chip inside the status cell
            var chip = statusCell.Locator(".mud-chip");
            return (await chip.TextContentAsync())?.Trim();
        }
        catch (TimeoutException)
        {
            return null;
        }
    }

    public async Task<bool> IsTableVisibleAsync()
    {
        try
        {
            await MarketplacesTable.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    // ─── Page State ────────────────────────────────────────────────────────

    public bool IsOnMarketplacesListPage()
    {
        return _page.Url.Contains("/marketplaces") && !_page.Url.Contains("/category-mappings");
    }
}
