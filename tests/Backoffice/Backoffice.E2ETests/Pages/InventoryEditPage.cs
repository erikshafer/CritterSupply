namespace Backoffice.E2ETests.Pages;

/// <summary>
/// Page Object Model for the Backoffice Inventory Edit page.
/// Covers inventory adjustment, stock receipt, KPI display, and validation.
/// </summary>
public sealed class InventoryEditPage
{
    private readonly IPage _page;
    private readonly string _baseUrl;

    public InventoryEditPage(IPage page, string baseUrl)
    {
        _page = page;
        _baseUrl = baseUrl;
    }

    // Locators - Page Elements
    private ILocator PageTitle => _page.GetByTestId("inventory-edit-title");

    // Locators - KPI Cards
    private ILocator AvailableValue => _page.GetByTestId("kpi-available-value");
    private ILocator ReservedValue => _page.GetByTestId("kpi-reserved-value");
    private ILocator TotalValue => _page.GetByTestId("kpi-total-value");

    // Locators - Adjust Inventory Form
    private ILocator AdjustForm => _page.GetByTestId("adjust-inventory-form");
    private ILocator AdjustQuantity => _page.GetByTestId("adjustment-quantity");
    private ILocator AdjustReason => _page.GetByTestId("adjustment-reason");
    private ILocator AdjustSubmit => _page.GetByTestId("adjust-submit");

    // Locators - Receive Stock Form
    private ILocator ReceiveForm => _page.GetByTestId("receive-stock-form");
    private ILocator ReceiveQuantity => _page.GetByTestId("receive-quantity");
    private ILocator ReceiveSource => _page.GetByTestId("receive-source");
    private ILocator ReceiveSubmit => _page.GetByTestId("receive-submit");

    // Locators - Feedback
    private ILocator SuccessMessage => _page.GetByTestId("success-message");
    private ILocator ErrorMessage => _page.GetByTestId("error-message");

    // Actions - Navigation
    public async Task NavigateAsync(string sku)
    {
        await _page.GotoAsync($"{_baseUrl}/inventory/{sku}/edit", new PageGotoOptions { WaitUntil = WaitUntilState.Commit });
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await PageTitle.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
    }

    // Actions - Adjust Inventory
    public async Task SetAdjustmentQuantityAsync(string quantity)
    {
        var input = AdjustQuantity.Locator("input");
        await input.ClearAsync();
        await input.FillAsync(quantity);
    }

    public async Task SelectAdjustmentReasonAsync(string reason)
    {
        // MudSelect requires clicking to open the dropdown, then selecting an item
        var selectInput = AdjustReason.Locator("div.mud-input-control");
        await selectInput.ClickAsync();
        // Wait for popover to appear
        await _page.WaitForTimeoutAsync(300);
        // Click the menu item matching the reason text
        var menuItem = _page.Locator($".mud-popover-provider .mud-list-item:has-text('{reason}')");
        await menuItem.ClickAsync();
        // Wait for selection to register
        await _page.WaitForTimeoutAsync(300);
    }

    public async Task SubmitAdjustmentAsync()
    {
        await AdjustSubmit.ClickAsync();
        // Wait for server response
        await _page.WaitForTimeoutAsync(1000);
    }

    // Actions - Receive Stock
    public async Task SetReceiveQuantityAsync(string quantity)
    {
        var input = ReceiveQuantity.Locator("input");
        await input.ClearAsync();
        await input.FillAsync(quantity);
    }

    public async Task SetReceiveSourceAsync(string source)
    {
        var input = ReceiveSource.Locator("input");
        await input.ClearAsync();
        await input.FillAsync(source);
    }

    public async Task SubmitReceiveAsync()
    {
        await ReceiveSubmit.ClickAsync();
        // Wait for server response
        await _page.WaitForTimeoutAsync(1000);
    }

    // Assertions - KPI Values
    public async Task<string> GetAvailableQuantityAsync()
    {
        return (await AvailableValue.InnerTextAsync()).Trim();
    }

    public async Task<string> GetReservedQuantityAsync()
    {
        return (await ReservedValue.InnerTextAsync()).Trim();
    }

    public async Task<string> GetTotalQuantityAsync()
    {
        return (await TotalValue.InnerTextAsync()).Trim();
    }

    // Assertions - Form Visibility
    public async Task<bool> IsAdjustFormVisibleAsync()
    {
        return await AdjustForm.IsVisibleAsync();
    }

    public async Task<bool> IsReceiveFormVisibleAsync()
    {
        return await ReceiveForm.IsVisibleAsync();
    }

    // Assertions - Button State
    public async Task<bool> IsAdjustSubmitDisabledAsync()
    {
        return await AdjustSubmit.IsDisabledAsync();
    }

    public async Task<bool> IsReceiveSubmitDisabledAsync()
    {
        return await ReceiveSubmit.IsDisabledAsync();
    }

    // Assertions - Page State
    public async Task<bool> IsOnPageForSkuAsync(string sku)
    {
        var url = _page.Url;
        return url.Contains($"/inventory/{sku}/edit");
    }

    // Assertions - Feedback
    public async Task<string> GetSuccessMessageAsync()
    {
        try
        {
            return await SuccessMessage.InnerTextAsync();
        }
        catch
        {
            return string.Empty;
        }
    }

    public async Task<string> GetErrorMessageAsync()
    {
        try
        {
            return await ErrorMessage.InnerTextAsync();
        }
        catch
        {
            return string.Empty;
        }
    }
}
