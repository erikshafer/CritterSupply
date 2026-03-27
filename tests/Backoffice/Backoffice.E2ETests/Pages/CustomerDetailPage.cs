using Microsoft.Playwright;

namespace Backoffice.E2ETests.Pages;

/// <summary>
/// Page Object Model for the Backoffice Customer Detail page.
/// Covers customer info, addresses table, order history, and navigation.
/// Route: /customers/{customerId:guid}
/// </summary>
public sealed class CustomerDetailPage
{
    private readonly IPage _page;
    private readonly string _baseUrl;

    private const int ApiCallTimeoutMs = 15_000;

    public CustomerDetailPage(IPage page, string baseUrl)
    {
        _page = page;
        _baseUrl = baseUrl;
    }

    // Locators - Page Structure
    private ILocator PageHeading => _page.GetByTestId("page-heading");
    private ILocator BackToSearchButton => _page.GetByTestId("back-to-search-btn");
    private ILocator LoadingIndicator => _page.GetByTestId("customer-loading");
    private ILocator NotFoundAlert => _page.GetByTestId("customer-not-found");

    // Locators - Customer Info
    private ILocator CustomerId => _page.GetByTestId("customer-id");
    private ILocator CustomerEmail => _page.GetByTestId("customer-email");
    private ILocator CustomerFirstName => _page.GetByTestId("customer-first-name");
    private ILocator CustomerLastName => _page.GetByTestId("customer-last-name");
    private ILocator CustomerCreatedAt => _page.GetByTestId("customer-created-at");

    // Locators - Addresses Table
    private ILocator AddressesTable => _page.GetByTestId("addresses-table");

    // Locators - Orders Table
    private ILocator OrdersTable => _page.GetByTestId("orders-table");
    private ILocator NoOrdersAlert => _page.GetByTestId("no-orders");

    // Actions - Navigation
    public async Task NavigateAsync(Guid customerId)
    {
        await _page.GotoAsync($"{_baseUrl}/customers/{customerId}", new PageGotoOptions { WaitUntil = WaitUntilState.Commit });
        await WaitForPageLoadedAsync();
    }

    public async Task WaitForPageLoadedAsync()
    {
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        try
        {
            await _page.WaitForSelectorAsync(
                "[data-testid='customer-email'], [data-testid='customer-not-found'], [data-testid='session-expired']",
                new() { Timeout = ApiCallTimeoutMs });
        }
        catch (TimeoutException)
        {
            // Acceptable — assertions will catch actual failures
        }
    }

    public async Task ClickBackToSearchAsync()
    {
        await BackToSearchButton.ClickAsync();
        await _page.WaitForURLAsync(
            url => url.Contains("/customers/search"),
            new() { Timeout = 5_000, WaitUntil = WaitUntilState.Commit });
    }

    // Assertions - Page State
    public bool IsOnCustomerDetailPage()
    {
        return _page.Url.Contains("/customers/") && !_page.Url.Contains("/customers/search");
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

    public async Task<bool> IsNotFoundVisibleAsync()
    {
        try
        {
            await NotFoundAlert.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 2_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    // Assertions - Customer Info
    public async Task<string?> GetFirstNameAsync()
    {
        return (await CustomerFirstName.TextContentAsync())?.Trim();
    }

    public async Task<string?> GetLastNameAsync()
    {
        return (await CustomerLastName.TextContentAsync())?.Trim();
    }

    public async Task<string?> GetEmailAsync()
    {
        return (await CustomerEmail.TextContentAsync())?.Trim();
    }

    public async Task<string?> GetMemberSinceAsync()
    {
        return (await CustomerCreatedAt.TextContentAsync())?.Trim();
    }

    // Assertions - Orders
    public async Task<int> GetOrderCountAsync()
    {
        try
        {
            await OrdersTable.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
            var rows = await OrdersTable.Locator("tbody tr").AllAsync();
            return rows.Count;
        }
        catch (TimeoutException)
        {
            return 0;
        }
    }

    public async Task<bool> IsNoOrdersMessageVisibleAsync()
    {
        try
        {
            await NoOrdersAlert.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 2_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    // Assertions - Addresses
    public async Task<int> GetAddressCountAsync()
    {
        try
        {
            await AddressesTable.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
            var rows = await AddressesTable.Locator("tbody tr").AllAsync();
            return rows.Count;
        }
        catch (TimeoutException)
        {
            return 0;
        }
    }

    // Actions - Order Navigation
    public async Task ClickViewOrderAsync(Guid orderId)
    {
        var viewOrderButton = _page.GetByTestId($"view-order-{orderId}");
        await viewOrderButton.ClickAsync();
        await _page.WaitForURLAsync(
            url => url.Contains($"/orders/{orderId}"),
            new() { Timeout = 5_000, WaitUntil = WaitUntilState.Commit });
    }

    public async Task ClickFirstOrderAsync()
    {
        var firstOrderLink = OrdersTable.Locator("tbody tr").First.Locator("[data-testid^='view-order-']");
        await firstOrderLink.ClickAsync();
        await _page.WaitForURLAsync(
            url => url.Contains("/orders/") && !url.Contains("/orders/search"),
            new() { Timeout = 5_000, WaitUntil = WaitUntilState.Commit });
    }
}
