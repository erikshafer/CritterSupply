using Microsoft.Playwright;

namespace Backoffice.E2ETests.Pages;

/// <summary>
/// Page Object Model for the Backoffice Customer Service view.
/// Covers customer lookup by email, order history table, return request details.
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

    // Locators - Search Form
    private ILocator EmailSearchInput => _page.GetByTestId("customer-search-email");
    private ILocator SearchButton => _page.GetByTestId("customer-search-submit");
    private ILocator SearchLoading => _page.GetByTestId("customer-search-loading");
    private ILocator NoResultsMessage => _page.GetByTestId("customer-search-no-results");

    // Locators - Customer Details
    private ILocator CustomerCard => _page.GetByTestId("customer-details-card");
    private ILocator CustomerName => _page.GetByTestId("customer-name");
    private ILocator CustomerEmail => _page.GetByTestId("customer-email");
    private ILocator CustomerPhone => _page.GetByTestId("customer-phone");

    // Locators - Order History Table
    private ILocator OrderHistoryTable => _page.GetByTestId("order-history-table");
    private ILocator OrderHistoryRows => OrderHistoryTable.Locator("tbody tr");
    private ILocator OrderHistoryEmpty => _page.GetByTestId("order-history-empty");

    // Locators - Return Requests
    private ILocator ReturnRequestsSection => _page.GetByTestId("return-requests-section");
    private ILocator ReturnRequestRows => ReturnRequestsSection.Locator("[data-testid^='return-']");

    // Actions
    public async Task NavigateAsync()
    {
        await _page.GotoAsync($"{_baseUrl}/customers/search", new PageGotoOptions { WaitUntil = WaitUntilState.Commit });

        // Wait for customer service page to be fully loaded
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await EmailSearchInput.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
    }

    public async Task SearchByEmailAsync(string email)
    {
        await EmailSearchInput.FillAsync(email);
        await SearchButton.ClickAsync();

        // Wait for either results or "no results" message
        await _page.WaitForSelectorAsync(
            "[data-testid='customer-details-card'], [data-testid='customer-search-no-results']",
            new() { Timeout = 10_000 }
        );
    }

    public async Task SearchByEmailAndWaitForResultsAsync(string email)
    {
        await EmailSearchInput.FillAsync(email);
        await SearchButton.ClickAsync();

        // Wait for customer details card to appear
        await CustomerCard.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        // Wait for order history table to finish loading (may be empty, but section should be visible)
        await OrderHistoryTable.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
    }

    // Assertions - Customer Details
    public async Task<bool> IsCustomerFoundAsync()
    {
        try
        {
            await CustomerCard.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 2_000 });
            return true;
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

    public async Task<string?> GetCustomerNameAsync()
    {
        if (await IsCustomerFoundAsync())
        {
            return await CustomerName.TextContentAsync();
        }
        return null;
    }

    public async Task<string?> GetCustomerEmailAsync()
    {
        if (await IsCustomerFoundAsync())
        {
            return await CustomerEmail.TextContentAsync();
        }
        return null;
    }

    public async Task<string?> GetCustomerPhoneAsync()
    {
        if (await IsCustomerFoundAsync())
        {
            return await CustomerPhone.TextContentAsync();
        }
        return null;
    }

    // Assertions - Order History
    public async Task<int> GetOrderHistoryCountAsync()
    {
        try
        {
            await OrderHistoryTable.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 2_000 });
            return await OrderHistoryRows.CountAsync();
        }
        catch (TimeoutException)
        {
            return 0;
        }
    }

    public async Task<bool> IsOrderHistoryEmptyAsync()
    {
        try
        {
            await OrderHistoryEmpty.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 2_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<string>> GetOrderIdsAsync()
    {
        var orderIds = new List<string>();
        var count = await GetOrderHistoryCountAsync();

        for (int i = 0; i < count; i++)
        {
            var row = OrderHistoryRows.Nth(i);
            var orderIdCell = row.Locator("[data-testid='order-id']");
            var orderId = await orderIdCell.TextContentAsync();
            if (!string.IsNullOrWhiteSpace(orderId))
            {
                orderIds.Add(orderId.Trim());
            }
        }

        return orderIds;
    }

    public async Task ClickOrderAsync(string orderId)
    {
        var row = _page.GetByTestId($"order-{orderId}");
        await row.ClickAsync();

        // Wait for order details modal or navigation
        await _page.WaitForSelectorAsync(
            "[data-testid='order-details-modal'], [data-testid='order-details-page']",
            new() { Timeout = 5_000 }
        );
    }

    // Assertions - Return Requests
    public async Task<int> GetReturnRequestCountAsync()
    {
        try
        {
            await ReturnRequestsSection.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 2_000 });
            return await ReturnRequestRows.CountAsync();
        }
        catch (TimeoutException)
        {
            return 0;
        }
    }

    public async Task<IReadOnlyList<string>> GetReturnRequestStatusesAsync()
    {
        var statuses = new List<string>();
        var count = await GetReturnRequestCountAsync();

        for (int i = 0; i < count; i++)
        {
            var row = ReturnRequestRows.Nth(i);
            var statusCell = row.Locator("[data-testid='return-status']");
            var status = await statusCell.TextContentAsync();
            if (!string.IsNullOrWhiteSpace(status))
            {
                statuses.Add(status.Trim());
            }
        }

        return statuses;
    }

    public async Task ClickReturnRequestAsync(string returnId)
    {
        var returnRow = _page.GetByTestId($"return-{returnId}");
        await returnRow.ClickAsync();

        // Wait for return details modal or navigation
        await _page.WaitForSelectorAsync(
            "[data-testid='return-details-modal'], [data-testid='return-details-page']",
            new() { Timeout = 5_000 }
        );
    }

    public async Task ApproveReturnAsync(string returnId)
    {
        // Click return to open details
        await ClickReturnRequestAsync(returnId);

        // Click approve button
        var approveButton = _page.GetByTestId("return-approve-button");
        await approveButton.ClickAsync();

        // Wait for confirmation or status change
        await _page.WaitForSelectorAsync(
            "[data-testid='return-approved-confirmation'], [data-testid='return-status'][text='Approved']",
            new() { Timeout = 5_000 }
        );
    }

    public async Task DenyReturnAsync(string returnId, string reason)
    {
        // Click return to open details
        await ClickReturnRequestAsync(returnId);

        // Fill denial reason
        var reasonInput = _page.GetByTestId("return-denial-reason");
        await reasonInput.FillAsync(reason);

        // Click deny button
        var denyButton = _page.GetByTestId("return-deny-button");
        await denyButton.ClickAsync();

        // Wait for confirmation or status change
        await _page.WaitForSelectorAsync(
            "[data-testid='return-denied-confirmation'], [data-testid='return-status'][text='Denied']",
            new() { Timeout = 5_000 }
        );
    }

    public async Task<bool> IsOnCustomerServicePageAsync()
    {
        return _page.Url.Contains("/customers/search");
    }

    // SessionExpirySteps support methods
    public async Task SearchAsync(string email)
    {
        await SearchByEmailAsync(email);
    }

    public async Task<bool> IsSearchFormVisibleAsync()
    {
        try
        {
            await EmailSearchInput.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 2_000 });
            await SearchButton.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 2_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }
}
