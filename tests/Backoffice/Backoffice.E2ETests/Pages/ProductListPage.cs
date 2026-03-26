using Microsoft.Playwright;

namespace Backoffice.E2ETests.Pages;

/// <summary>
/// Page Object Model for the Backoffice Product List page.
/// Covers product browsing, search filtering, and navigation to product edit.
/// </summary>
public sealed class ProductListPage
{
    private readonly IPage _page;
    private readonly string _baseUrl;

    public ProductListPage(IPage page, string baseUrl)
    {
        _page = page;
        _baseUrl = baseUrl;
    }

    // Locators
    private ILocator ProductTable => _page.GetByTestId("product-table");
    private ILocator SearchField => _page.Locator("input[placeholder='Search by SKU or name...']");
    private ILocator LoadingIndicator => _page.Locator(".mud-progress-linear");

    // Actions
    public async Task NavigateAsync()
    {
        await _page.GotoAsync($"{_baseUrl}/products", new PageGotoOptions { WaitUntil = WaitUntilState.Commit });

        // Wait for page to be fully loaded
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await ProductTable.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
    }

    public async Task SearchForProductAsync(string searchTerm)
    {
        await SearchField.FillAsync(searchTerm);
        // Wait a moment for client-side filtering to complete
        await _page.WaitForTimeoutAsync(500);
    }

    public async Task ClickEditForProductAsync(string sku)
    {
        var editButton = _page.GetByTestId($"edit-product-{sku}");
        await editButton.ClickAsync();
        // Wait for navigation to edit page
        await _page.WaitForURLAsync(url => url.Contains($"/products/{sku}/edit"), new() { Timeout = 10_000, WaitUntil = WaitUntilState.Commit });
    }

    // Assertions
    public async Task<bool> IsProductVisibleAsync(string sku)
    {
        var productStatus = _page.GetByTestId($"product-status-{sku}");
        return await productStatus.IsVisibleAsync();
    }

    public async Task<int> GetVisibleProductCountAsync()
    {
        // Count rows in the table (excluding header)
        var rows = _page.Locator("table tbody tr");
        return await rows.CountAsync();
    }

    public async Task<bool> IsProductTableVisibleAsync()
    {
        return await ProductTable.IsVisibleAsync();
    }

    public async Task<bool> IsLoadingAsync()
    {
        return await LoadingIndicator.IsVisibleAsync();
    }

    public async Task<IReadOnlyList<string>> GetVisibleProductSkusAsync()
    {
        // Get all SKU cells (first column)
        var skuCells = _page.Locator("table tbody tr td:first-child strong");
        var count = await skuCells.CountAsync();

        var skus = new List<string>();
        for (int i = 0; i < count; i++)
        {
            var text = await skuCells.Nth(i).TextContentAsync();
            if (!string.IsNullOrEmpty(text))
            {
                skus.Add(text.Trim());
            }
        }

        return skus;
    }
}
