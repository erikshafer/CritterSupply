using Microsoft.Playwright;

namespace Backoffice.E2ETests.Pages;

/// <summary>
/// Page Object Model for the Backoffice Order Search page.
/// Covers order search by GUID, results display, and navigation to order detail.
/// Route: /orders/search
/// </summary>
public sealed class OrderSearchPage
{
    private readonly IPage _page;
    private readonly string _baseUrl;

    private const int WasmBootstrapTimeoutMs = 60_000;
    private const int ApiCallTimeoutMs = 15_000;

    public OrderSearchPage(IPage page, string baseUrl)
    {
        _page = page;
        _baseUrl = baseUrl;
    }

    // Locators — match actual OrderSearch.razor data-testid attributes
    private ILocator SearchInput => _page.GetByTestId("order-search-input");
    private ILocator SearchButton => _page.GetByTestId("search-order-btn");
    private ILocator NoResultsAlert => _page.GetByTestId("no-results-alert");
    private ILocator ResultsTable => _page.GetByTestId("order-results-table");

    // ─── Navigation ────────────────────────────────────────────────────────

    public async Task NavigateAsync()
    {
        await _page.GotoAsync($"{_baseUrl}/orders/search", new PageGotoOptions { WaitUntil = WaitUntilState.Commit });
        await WaitForPageLoadedAsync();
    }

    public async Task WaitForPageLoadedAsync()
    {
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await SearchInput.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = WasmBootstrapTimeoutMs });
    }

    // ─── Search ────────────────────────────────────────────────────────────

    public async Task FillSearchAsync(string query)
    {
        await SearchInput.FillAsync(query);
    }

    public async Task ClickSearchAsync()
    {
        await SearchButton.ClickAsync();

        // Wait for either results table or "no results" alert
        await _page.WaitForSelectorAsync(
            "[data-testid='order-results-table'], [data-testid='no-results-alert']",
            new() { Timeout = ApiCallTimeoutMs });
    }

    public async Task SearchOrderAsync(string query)
    {
        await FillSearchAsync(query);
        await ClickSearchAsync();
    }

    // ─── Results ───────────────────────────────────────────────────────────

    public async Task WaitForResultsAsync()
    {
        await ResultsTable.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = ApiCallTimeoutMs });
    }

    public async Task<int> GetResultCountAsync()
    {
        try
        {
            await ResultsTable.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
            var rows = await ResultsTable.Locator("tbody tr").AllAsync();
            return rows.Count;
        }
        catch (TimeoutException)
        {
            return 0;
        }
    }

    public async Task<bool> IsNoResultsAlertVisibleAsync()
    {
        try
        {
            await NoResultsAlert.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    // ─── View Details ──────────────────────────────────────────────────────

    public async Task ClickViewOrderAsync(Guid orderId)
    {
        var viewLink = _page.GetByTestId($"view-order-{orderId}");
        await viewLink.ClickAsync();

        await _page.WaitForURLAsync(
            url => url.Contains($"/orders/{orderId}"),
            new() { Timeout = 5_000, WaitUntil = WaitUntilState.Commit });
    }

    // ─── Page State ────────────────────────────────────────────────────────

    public bool IsOnOrderSearchPage()
    {
        return _page.Url.Contains("/orders/search");
    }
}
