namespace Backoffice.E2ETests.Pages;

/// <summary>
/// Page Object Model for the Backoffice Return Detail page.
/// Covers return detail view, approve/deny actions, and navigation.
/// </summary>
public sealed class ReturnDetailPage
{
    private readonly IPage _page;
    private readonly string _baseUrl;

    private const int WasmHydrationTimeoutMs = 30_000;
    private const int ApiCallTimeoutMs = 15_000;

    public ReturnDetailPage(IPage page, string baseUrl)
    {
        _page = page;
        _baseUrl = baseUrl;
    }

    // Locators - Page Structure
    private ILocator PageHeading => _page.GetByTestId("page-heading");
    private ILocator ReturnId => _page.GetByTestId("return-id");
    private ILocator ReturnOrderId => _page.GetByTestId("return-order-id");
    private ILocator ReturnStatus => _page.GetByTestId("return-status");
    private ILocator ReturnType => _page.GetByTestId("return-type");
    private ILocator ReturnReason => _page.GetByTestId("return-reason");
    private ILocator ReturnItemsTable => _page.GetByTestId("return-items-table");
    private ILocator DenialReason => _page.GetByTestId("denial-reason");
    private ILocator LoadingIndicator => _page.GetByTestId("return-loading");
    private ILocator NotFoundAlert => _page.GetByTestId("return-not-found");
    private ILocator BackButton => _page.GetByTestId("back-to-returns-btn");

    // Locators - Actions
    private ILocator ApproveButton => _page.GetByTestId("approve-return-btn");
    private ILocator DenyButton => _page.GetByTestId("deny-return-btn");
    private ILocator DenyReasonInput => _page.GetByTestId("deny-reason-input");
    private ILocator ConfirmDenyButton => _page.GetByTestId("confirm-deny-btn");
    private ILocator CancelDenyButton => _page.GetByTestId("cancel-deny-btn");

    // Actions - Navigation
    public async Task NavigateAsync(Guid returnId)
    {
        await _page.GotoAsync($"{_baseUrl}/returns/{returnId}", new PageGotoOptions { WaitUntil = WaitUntilState.Commit });
        await WaitForPageLoadedAsync();
    }

    public async Task WaitForPageLoadedAsync()
    {
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        try
        {
            await _page.WaitForSelectorAsync(
                "[data-testid='return-id'], [data-testid='return-not-found'], [data-testid='session-expired']",
                new() { Timeout = ApiCallTimeoutMs });
        }
        catch (TimeoutException)
        {
            // Acceptable — assertions will catch actual failures
        }
    }

    // Actions - Approve/Deny
    public async Task ClickApproveAsync()
    {
        await ApproveButton.ClickAsync();

        // Wait for API call to complete and page to reload
        try
        {
            await _page.WaitForSelectorAsync("[data-testid='return-status']",
                new() { Timeout = ApiCallTimeoutMs });
        }
        catch (TimeoutException)
        {
            // Acceptable — assertions will verify outcome
        }
    }

    public async Task ClickDenyAsync(string reason)
    {
        await DenyButton.ClickAsync();

        // Wait for dialog to appear
        await DenyReasonInput.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });

        // Fill reason
        await DenyReasonInput.Locator("textarea").FillAsync(reason);

        // Confirm deny
        await ConfirmDenyButton.ClickAsync();

        // Wait for API call to complete
        try
        {
            await _page.WaitForSelectorAsync("[data-testid='return-status']",
                new() { Timeout = ApiCallTimeoutMs });
        }
        catch (TimeoutException)
        {
            // Acceptable — assertions will verify outcome
        }
    }

    public async Task ClickBackAsync()
    {
        await BackButton.ClickAsync();
        await _page.WaitForURLAsync(
            url => url.Contains("/returns") && !url.Contains("/returns/"),
            new() { Timeout = 5_000, WaitUntil = WaitUntilState.Commit });
    }

    // Assertions
    public async Task<bool> IsOnReturnDetailPageAsync()
    {
        return _page.Url.Contains("/returns/") && !_page.Url.EndsWith("/returns");
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

    public async Task<string?> GetReturnStatusAsync()
    {
        try
        {
            await ReturnStatus.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
            return (await ReturnStatus.TextContentAsync())?.Trim();
        }
        catch (TimeoutException)
        {
            return null;
        }
    }

    public async Task<string?> GetReturnTypeAsync()
    {
        return (await ReturnType.TextContentAsync())?.Trim();
    }

    public async Task<string?> GetReturnReasonAsync()
    {
        return (await ReturnReason.TextContentAsync())?.Trim();
    }

    public async Task<bool> IsApproveButtonVisibleAsync()
    {
        try
        {
            await ApproveButton.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 2_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    public async Task<bool> IsDenyButtonVisibleAsync()
    {
        try
        {
            await DenyButton.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 2_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
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

    public async Task<bool> IsDenialReasonVisibleAsync()
    {
        try
        {
            await DenialReason.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 2_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    public async Task<int> GetReturnItemCountAsync()
    {
        try
        {
            var rows = await ReturnItemsTable.Locator("tbody tr").AllAsync();
            return rows.Count;
        }
        catch
        {
            return 0;
        }
    }
}
