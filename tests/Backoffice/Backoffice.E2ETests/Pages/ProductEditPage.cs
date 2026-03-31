namespace Backoffice.E2ETests.Pages;

/// <summary>
/// Page Object Model for the Backoffice Product Edit page.
/// Covers role-based field permissions, product editing, discontinuation workflow, and change tracking.
/// </summary>
public sealed class ProductEditPage
{
    private readonly IPage _page;
    private readonly string _baseUrl;

    public ProductEditPage(IPage page, string baseUrl)
    {
        _page = page;
        _baseUrl = baseUrl;
    }

    // Locators - Form Fields
    private ILocator SkuField => _page.GetByTestId("product-sku");
    private ILocator DisplayNameInput => _page.GetByTestId("product-name-input");
    private ILocator DescriptionInput => _page.GetByTestId("product-description-input");
    private ILocator CategoryField => _page.GetByTestId("product-category");
    private ILocator StatusField => _page.GetByTestId("product-status");

    // Locators - Action Buttons
    private ILocator SaveButton => _page.GetByTestId("save-btn");
    private ILocator DiscontinueButton => _page.GetByTestId("discontinue-btn");
    private ILocator CancelButton => _page.GetByTestId("cancel-btn");

    // Locators - Feedback
    private ILocator LoadingIndicator => _page.Locator(".mud-progress-linear");
    private ILocator SuccessMessage => _page.Locator(".mud-snackbar-content-message"); // MudSnackbar success toast
    private ILocator WarningDialog => _page.Locator(".mud-dialog"); // MudDialog for discontinue warning

    // Actions
    public async Task NavigateAsync(string sku)
    {
        await _page.GotoAsync($"{_baseUrl}/products/{sku}/edit", new PageGotoOptions { WaitUntil = WaitUntilState.Commit });

        // Wait for page to be fully loaded
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await SkuField.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
    }

    public async Task WaitForPageLoadAsync()
    {
        // Wait for loading indicator to disappear
        await _page.WaitForSelectorAsync(".mud-progress-linear", new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
    }

    public async Task SetDisplayNameAsync(string displayName)
    {
        await DisplayNameInput.FillAsync(displayName);
    }

    public async Task SetDescriptionAsync(string description)
    {
        await DescriptionInput.FillAsync(description);
    }

    public async Task ClickSaveAsync()
    {
        await SaveButton.ClickAsync();
        // Wait for save operation to complete (button disabled state changes)
        await _page.WaitForTimeoutAsync(1000);
    }

    public async Task ClickDiscontinueAsync()
    {
        await DiscontinueButton.ClickAsync();
    }

    public async Task ClickCancelAsync()
    {
        await CancelButton.ClickAsync();
    }

    // Assertions - Field State
    public async Task<bool> IsDisplayNameEnabledAsync()
    {
        return !(await DisplayNameInput.IsDisabledAsync());
    }

    public async Task<bool> IsDescriptionEnabledAsync()
    {
        return !(await DescriptionInput.IsDisabledAsync());
    }

    public async Task<bool> IsDiscontinueButtonVisibleAsync()
    {
        return await DiscontinueButton.IsVisibleAsync();
    }

    public async Task<bool> IsSaveButtonDisabledAsync()
    {
        return await SaveButton.IsDisabledAsync();
    }

    // Assertions - Field Values
    public async Task<string?> GetSkuAsync()
    {
        return await SkuField.InputValueAsync();
    }

    public async Task<string?> GetDisplayNameAsync()
    {
        return await DisplayNameInput.InputValueAsync();
    }

    public async Task<string?> GetDescriptionAsync()
    {
        return await DescriptionInput.InputValueAsync();
    }

    public async Task<string?> GetStatusAsync()
    {
        return await StatusField.InputValueAsync();
    }

    // Assertions - Feedback
    public async Task<bool> IsSuccessMessageVisibleAsync()
    {
        return await SuccessMessage.IsVisibleAsync();
    }

    public async Task<string?> GetSuccessMessageTextAsync()
    {
        return await SuccessMessage.TextContentAsync();
    }

    public async Task<bool> IsWarningDialogVisibleAsync()
    {
        return await WarningDialog.IsVisibleAsync();
    }

    public async Task<bool> IsOnPageForSkuAsync(string sku)
    {
        var currentUrl = _page.Url;
        return currentUrl.Contains($"/products/{sku}/edit");
    }
}
