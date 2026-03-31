namespace Backoffice.E2ETests.Pages;

/// <summary>
/// Page Object Model for the Backoffice Customer Search page.
/// Covers customer search by name/email/ID and navigation to the customer detail page.
/// Route: /customers/search
///
/// Session 3 fix: Consolidated stale locators (customer-search-email, customer-search-submit,
/// customer-details-card, order-history-table, return-requests-section) to match actual
/// CustomerSearch.razor data-testid attributes. Removed methods for inline details, order
/// history, and return requests — those live on CustomerDetail and ReturnManagement pages.
/// Classification: test bug (aspirational locators written before pages were implemented).
/// </summary>
public sealed class CustomerSearchPage
{
    private readonly IPage _page;
    private readonly string _baseUrl;

    public CustomerSearchPage(IPage page, string baseUrl)
    {
        _page = page;
        _baseUrl = baseUrl;
    }

    // Locators — match actual CustomerSearch.razor data-testid attributes
    private ILocator SearchInput => _page.GetByTestId("customer-search-input");
    private ILocator SearchButton => _page.GetByTestId("search-btn");
    private ILocator NoResultsMessage => _page.GetByTestId("customer-search-no-results");
    private ILocator ResultsTable => _page.GetByTestId("customer-results-table");

    // ─── Navigation ────────────────────────────────────────────────────────

    public async Task NavigateAsync()
    {
        await _page.GotoAsync($"{_baseUrl}/customers/search", new PageGotoOptions { WaitUntil = WaitUntilState.Commit });
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await SearchInput.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
    }

    /// <summary>Alias for <see cref="NavigateAsync"/>.</summary>
    public Task NavigateToSearchPageAsync() => NavigateAsync();

    // ─── Search ────────────────────────────────────────────────────────────

    public async Task SearchByEmailAsync(string email)
    {
        await SearchInput.FillAsync(email);
        await SearchButton.ClickAsync();

        // Wait for either results table or "no results" message
        await _page.WaitForSelectorAsync(
            "[data-testid='customer-results-table'], [data-testid='customer-search-no-results']",
            new() { Timeout = 10_000 });
    }

    /// <summary>Alias for <see cref="SearchByEmailAsync"/>.</summary>
    public Task PerformSearchAsync(string query) => SearchByEmailAsync(query);

    /// <summary>Alias for <see cref="SearchByEmailAsync"/> (used by SessionExpirySteps).</summary>
    public Task SearchAsync(string email) => SearchByEmailAsync(email);

    // ─── View Details ──────────────────────────────────────────────────────

    public async Task ClickViewDetailsAsync(Guid customerId)
    {
        var viewButton = _page.GetByTestId($"view-customer-{customerId}");
        await viewButton.ClickAsync();

        await _page.WaitForURLAsync(
            url => url.Contains($"/customers/{customerId}"),
            new() { Timeout = 5_000, WaitUntil = WaitUntilState.Commit });
    }

    // ─── Assertions ────────────────────────────────────────────────────────

    public async Task<bool> HasSearchResultForNameAsync(string name)
    {
        try
        {
            await ResultsTable.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
            var content = await ResultsTable.TextContentAsync();
            return content?.Contains(name, StringComparison.OrdinalIgnoreCase) ?? false;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    public async Task<bool> IsNoResultsMessageVisibleAsync()
    {
        try
        {
            await NoResultsMessage.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 2_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    public async Task<bool> IsNoSearchResultsFoundAsync()
    {
        try
        {
            await NoResultsMessage.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    // ─── Page State ────────────────────────────────────────────────────────

    public async Task<bool> IsOnCustomerServicePageAsync()
    {
        return _page.Url.Contains("/customers/search");
    }

    public bool IsOnCustomerSearchPage()
    {
        return _page.Url.Contains("/customers/search");
    }

    public async Task<bool> IsSearchFormVisibleAsync()
    {
        try
        {
            await SearchInput.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 2_000 });
            await SearchButton.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 2_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }
}
