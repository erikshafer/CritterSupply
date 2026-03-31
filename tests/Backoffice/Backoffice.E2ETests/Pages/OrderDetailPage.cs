namespace Backoffice.E2ETests.Pages;

/// <summary>
/// Page Object Model for the Backoffice Order Detail page.
/// Covers order detail display, line items, and navigation.
/// </summary>
public sealed class OrderDetailPage
{
    private readonly IPage _page;
    private readonly string _baseUrl;

    private const int ApiCallTimeoutMs = 15_000;

    public OrderDetailPage(IPage page, string baseUrl)
    {
        _page = page;
        _baseUrl = baseUrl;
    }

    // Locators - Page Structure
    private ILocator PageHeading => _page.GetByTestId("page-heading");
    private ILocator OrderId => _page.GetByTestId("order-id");
    private ILocator CustomerEmail => _page.GetByTestId("customer-email");
    private ILocator OrderStatus => _page.GetByTestId("order-status");
    private ILocator OrderTotal => _page.GetByTestId("order-total");
    private ILocator OrderPlacedAt => _page.GetByTestId("order-placed-at");
    private ILocator LineItemsTable => _page.GetByTestId("line-items-table");
    private ILocator ReturnableItemsTable => _page.GetByTestId("returnable-items-table");
    private ILocator CancellationReason => _page.GetByTestId("cancellation-reason");
    private ILocator LoadingIndicator => _page.GetByTestId("order-loading");
    private ILocator NotFoundAlert => _page.GetByTestId("order-not-found");
    private ILocator BackButton => _page.GetByTestId("back-to-search-btn");

    // Actions - Navigation
    public async Task NavigateAsync(Guid orderId)
    {
        await _page.GotoAsync($"{_baseUrl}/orders/{orderId}", new PageGotoOptions { WaitUntil = WaitUntilState.Commit });
        await WaitForPageLoadedAsync();
    }

    public async Task WaitForPageLoadedAsync()
    {
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        try
        {
            await _page.WaitForSelectorAsync(
                "[data-testid='order-id'], [data-testid='order-not-found'], [data-testid='session-expired']",
                new() { Timeout = ApiCallTimeoutMs });
        }
        catch (TimeoutException)
        {
            // Acceptable — assertions will catch actual failures
        }
    }

    public async Task ClickBackAsync()
    {
        await BackButton.ClickAsync();
        await _page.WaitForURLAsync(
            url => url.Contains("/orders/search"),
            new() { Timeout = 5_000, WaitUntil = WaitUntilState.Commit });
    }

    // Assertions
    public async Task<bool> IsOnOrderDetailPageAsync()
    {
        return _page.Url.Contains("/orders/") && !_page.Url.Contains("/orders/search");
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

    public async Task<string?> GetOrderStatusAsync()
    {
        try
        {
            await OrderStatus.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
            return (await OrderStatus.TextContentAsync())?.Trim();
        }
        catch (TimeoutException)
        {
            return null;
        }
    }

    public async Task<string?> GetCustomerEmailAsync()
    {
        return (await CustomerEmail.TextContentAsync())?.Trim();
    }

    public async Task<string?> GetOrderTotalAsync()
    {
        return (await OrderTotal.TextContentAsync())?.Trim();
    }

    public async Task<bool> IsNotFoundAlertVisibleAsync()
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

    public async Task<int> GetLineItemCountAsync()
    {
        try
        {
            var rows = await LineItemsTable.Locator("tbody tr").AllAsync();
            return rows.Count;
        }
        catch
        {
            return 0;
        }
    }

    public async Task<bool> IsCancellationReasonVisibleAsync()
    {
        try
        {
            await CancellationReason.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 2_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }
}
