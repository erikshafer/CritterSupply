using Microsoft.Playwright;

namespace Backoffice.E2ETests.Pages;

/// <summary>
/// Page Object Model for the Backoffice Category Mappings List page.
/// Route: /marketplaces/category-mappings
/// Covers category mapping table display, channel filter interaction, and breadcrumb validation.
/// </summary>
public sealed class CategoryMappingsListPage
{
    private readonly IPage _page;
    private readonly string _baseUrl;

    private const int WasmBootstrapTimeoutMs = 60_000;
    private const int ApiCallTimeoutMs = 15_000;
    private const int MudSelectListboxTimeoutMs = 15_000;

    public CategoryMappingsListPage(IPage page, string baseUrl)
    {
        _page = page;
        _baseUrl = baseUrl;
    }

    // Locators — match CategoryMappingsList.razor data-testid attributes
    private ILocator PageContainer => _page.GetByTestId("category-mappings-page");
    private ILocator MappingsTable => _page.GetByTestId("category-mappings-table");
    private ILocator Breadcrumbs => _page.GetByTestId("category-mappings-breadcrumbs");
    private ILocator Title => _page.GetByTestId("category-mappings-title");
    private ILocator ChannelFilter => _page.GetByTestId("channel-filter");
    private ILocator ErrorAlert => _page.GetByTestId("category-mappings-error");
    private ILocator Loading => _page.GetByTestId("category-mappings-loading");

    // ─── Navigation ────────────────────────────────────────────────────────

    public async Task NavigateAsync()
    {
        await _page.GotoAsync($"{_baseUrl}/marketplaces/category-mappings", new PageGotoOptions { WaitUntil = WaitUntilState.Commit });
        await WaitForLoadedAsync();
    }

    public async Task WaitForLoadedAsync()
    {
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Wait for MudBlazor framework to hydrate (WASM pattern)
        await _page.WaitForSelectorAsync(".mud-dialog-provider",
            new() { State = WaitForSelectorState.Attached, Timeout = WasmBootstrapTimeoutMs });

        // Wait for the category mappings table to appear
        try
        {
            await MappingsTable.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = ApiCallTimeoutMs });
        }
        catch (TimeoutException)
        {
            // Table may not appear if no mappings exist — acceptable
        }
    }

    // ─── Table Assertions ──────────────────────────────────────────────────

    public async Task<int> GetRowCountAsync()
    {
        try
        {
            await MappingsTable.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
            var rows = await MappingsTable.Locator("tbody tr").AllAsync();
            // Filter out the "no records" row if present
            var dataRows = new List<ILocator>();
            foreach (var row in rows)
            {
                var testId = await row.Locator("td[data-testid^='mapping-row-']").CountAsync();
                if (testId > 0) dataRows.Add(row);
            }
            return dataRows.Count;
        }
        catch (TimeoutException)
        {
            return 0;
        }
    }

    public async Task<bool> IsTableVisibleAsync()
    {
        try
        {
            await MappingsTable.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    // ─── Filtering ─────────────────────────────────────────────────────────

    public async Task FilterByChannelAsync(string channelCode)
    {
        await ChannelFilter.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = ApiCallTimeoutMs });

        // Force-click inner trigger (MudBlazor pattern)
        await ChannelFilter.Locator(".mud-select-input")
            .ClickAsync(new LocatorClickOptions { Force = true });

        // Wait for MudBlazor listbox portal to render
        await _page.WaitForSelectorAsync("[role='listbox']",
            new() { Timeout = MudSelectListboxTimeoutMs });

        // Click the option by value text
        var optionLocator = _page.Locator($"[role='option']:has-text('{channelCode}')");
        await optionLocator.ClickAsync(new LocatorClickOptions { Timeout = 10_000 });

        // Wait for reload
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    // ─── Breadcrumb Assertions ─────────────────────────────────────────────

    public async Task<bool> IsBreadcrumbVisibleAsync()
    {
        try
        {
            await Breadcrumbs.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    public async Task<string?> GetBreadcrumbTextAsync()
    {
        try
        {
            await Breadcrumbs.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
            return await Breadcrumbs.TextContentAsync();
        }
        catch (TimeoutException)
        {
            return null;
        }
    }

    // ─── Page State ────────────────────────────────────────────────────────

    public bool IsOnCategoryMappingsPage()
    {
        return _page.Url.Contains("/marketplaces/category-mappings");
    }
}
