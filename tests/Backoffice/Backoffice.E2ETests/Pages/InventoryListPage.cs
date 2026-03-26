using Microsoft.Playwright;

namespace Backoffice.E2ETests.Pages;

/// <summary>
/// Page Object Model for the Backoffice Inventory List page.
/// Covers inventory browsing, search filtering, and navigation to edit page.
/// </summary>
public sealed class InventoryListPage
{
    private readonly IPage _page;
    private readonly string _baseUrl;

    public InventoryListPage(IPage page, string baseUrl)
    {
        _page = page;
        _baseUrl = baseUrl;
    }

    // Locators
    private ILocator InventoryTable => _page.GetByTestId("inventory-table");
    private ILocator SearchField => _page.GetByTestId("inventory-search");

    // Actions
    public async Task NavigateAsync()
    {
        await _page.GotoAsync($"{_baseUrl}/inventory", new PageGotoOptions { WaitUntil = WaitUntilState.Commit });
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await InventoryTable.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
    }

    public async Task SearchForSkuAsync(string searchTerm)
    {
        // MudTextField renders an <input> inside the component; locate it within the data-testid wrapper
        var input = SearchField.Locator("input");
        await input.FillAsync(searchTerm);
        // Wait for client-side filtering to complete
        await _page.WaitForTimeoutAsync(500);
    }

    public async Task ClickSkuAsync(string sku)
    {
        var row = _page.GetByTestId($"inventory-row-{sku}");
        await row.ClickAsync();
        // Wait for navigation to edit page
        await _page.WaitForURLAsync(url => url.Contains($"/inventory/{sku}/edit"), new() { Timeout = 10_000, WaitUntil = WaitUntilState.Commit });
    }

    // Assertions
    public async Task<bool> IsInventoryTableVisibleAsync()
    {
        return await InventoryTable.IsVisibleAsync();
    }

    public async Task<bool> IsSkuVisibleAsync(string sku)
    {
        var row = _page.GetByTestId($"inventory-row-{sku}");
        return await row.IsVisibleAsync();
    }

    public async Task<string> GetStatusForSkuAsync(string sku)
    {
        var statusChip = _page.GetByTestId($"inventory-status-{sku}");
        return await statusChip.InnerTextAsync();
    }

    public async Task<string> GetAvailableForSkuAsync(string sku)
    {
        var cell = _page.GetByTestId($"inventory-available-{sku}");
        return await cell.InnerTextAsync();
    }
}
