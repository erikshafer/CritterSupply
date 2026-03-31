namespace Backoffice.E2ETests.Pages;

/// <summary>
/// Page Object Model for the Backoffice Listings Admin page.
/// Route: /admin/listings
/// Covers listing table display, status filtering, pagination, and navigation to detail pages.
/// </summary>
public sealed class ListingsAdminPage
{
    private readonly IPage _page;
    private readonly string _baseUrl;

    private const int WasmBootstrapTimeoutMs = 60_000;
    private const int ApiCallTimeoutMs = 15_000;
    private const int MudSelectListboxTimeoutMs = 15_000;

    public ListingsAdminPage(IPage page, string baseUrl)
    {
        _page = page;
        _baseUrl = baseUrl;
    }

    // Locators — match ListingsAdmin.razor data-testid attributes
    private ILocator ListingsTable => _page.GetByTestId("listings-table");
    private ILocator StatusFilter => _page.GetByTestId("status-filter");

    // ─── Navigation ────────────────────────────────────────────────────────

    public async Task NavigateAsync()
    {
        await _page.GotoAsync($"{_baseUrl}/admin/listings", new PageGotoOptions { WaitUntil = WaitUntilState.Commit });
        await WaitForLoadedAsync();
    }

    public async Task WaitForLoadedAsync()
    {
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Wait for MudBlazor framework to hydrate (WASM pattern)
        await _page.WaitForSelectorAsync(".mud-dialog-provider",
            new() { State = WaitForSelectorState.Attached, Timeout = WasmBootstrapTimeoutMs });

        // Wait for the listings table to appear
        try
        {
            await ListingsTable.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = ApiCallTimeoutMs });
        }
        catch (TimeoutException)
        {
            // Table may not appear if no listings exist — acceptable
        }
    }

    // ─── Table Assertions ──────────────────────────────────────────────────

    public async Task<int> GetRowCountAsync()
    {
        try
        {
            await ListingsTable.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
            var rows = await ListingsTable.Locator("tbody tr[data-testid^='listing-row-']").AllAsync();
            return rows.Count;
        }
        catch (TimeoutException)
        {
            return 0;
        }
    }

    public async Task<bool> IsTableEmptyAsync()
    {
        var rowCount = await GetRowCountAsync();
        return rowCount == 0;
    }

    // ─── Filtering ─────────────────────────────────────────────────────────

    public async Task FilterByStatusAsync(string status)
    {
        await StatusFilter.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = ApiCallTimeoutMs });

        // Force-click inner trigger (MudBlazor pattern)
        await StatusFilter.Locator(".mud-select-input")
            .ClickAsync(new LocatorClickOptions { Force = true });

        // Wait for MudBlazor listbox portal to render
        await _page.WaitForSelectorAsync("[role='listbox']",
            new() { Timeout = MudSelectListboxTimeoutMs });

        // Click the option by value text
        var optionLocator = _page.Locator($"[role='option']:has-text('{status}')");
        await optionLocator.ClickAsync(new LocatorClickOptions { Timeout = 10_000 });

        // Wait for reload
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    // ─── Row Assertions ────────────────────────────────────────────────────

    public async Task<string?> GetListingStatusAsync(string listingId)
    {
        var row = _page.GetByTestId($"listing-row-{listingId}");
        try
        {
            await row.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
            // Navigate to the row's parent <tr> and find the status badge
            var statusChip = row.Locator("xpath=ancestor::tr").Locator("[data-testid^='listing-status-']");
            return await statusChip.TextContentAsync();
        }
        catch (TimeoutException)
        {
            return null;
        }
    }

    // ─── Detail Navigation ─────────────────────────────────────────────────

    public async Task ClickViewListingAsync(string listingId)
    {
        var row = _page.GetByTestId($"listing-row-{listingId}");
        var link = row.Locator("a");
        await link.ClickAsync();

        await _page.WaitForURLAsync(
            url => url.Contains($"/admin/listings/{listingId}"),
            new() { Timeout = 5_000, WaitUntil = WaitUntilState.Commit });
    }

    // ─── Page State ────────────────────────────────────────────────────────

    public bool IsOnListingsAdminPage()
    {
        return _page.Url.Contains("/admin/listings") && !_page.Url.Contains("/admin/listings/");
    }
}
