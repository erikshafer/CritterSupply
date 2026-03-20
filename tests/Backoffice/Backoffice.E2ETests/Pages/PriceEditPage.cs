using Microsoft.Playwright;

namespace Backoffice.E2ETests.Pages;

/// <summary>
/// Page Object Model for the Backoffice Price Edit page.
/// Covers pricing admin workflows: set base price, floor/ceiling constraints, validation.
/// </summary>
public sealed class PriceEditPage
{
    private readonly IPage _page;
    private readonly string _baseUrl;

    public PriceEditPage(IPage page, string baseUrl)
    {
        _page = page;
        _baseUrl = baseUrl;
    }

    // Locators - Display Elements
    private ILocator SkuField => _page.GetByTestId("price-sku");
    private ILocator CurrentPriceDisplay => _page.GetByTestId("current-price");
    private ILocator PriceEditForm => _page.GetByTestId("price-edit-form");

    // Locators - Form Fields
    private ILocator PriceInput => _page.GetByTestId("price-input");
    private ILocator ChangedByInput => _page.GetByTestId("changed-by-input");

    // Locators - Action Buttons
    private ILocator SubmitPriceButton => _page.GetByTestId("submit-price-button");
    private ILocator CancelButton => _page.GetByTestId("cancel-btn");

    // Locators - Feedback
    private ILocator SuccessMessage => _page.GetByTestId("success-message");
    private ILocator ErrorMessage => _page.GetByTestId("error-message");

    // Actions
    public async Task NavigateAsync(string sku)
    {
        await _page.GotoAsync($"{_baseUrl}/products/{sku}/price");
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await SkuField.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
    }

    public async Task SetPriceAsync(string price)
    {
        // Remove $ symbol if present
        var cleanPrice = price.Replace("$", "");
        await PriceInput.FillAsync(cleanPrice);
    }

    public async Task SubmitPriceAsync()
    {
        await SubmitPriceButton.ClickAsync();
        // Wait for server response
        await _page.WaitForTimeoutAsync(1000);
    }

    public async Task ClickCancelAsync()
    {
        await CancelButton.ClickAsync();
    }

    // Assertions - Display State
    public async Task<string> GetCurrentPriceAsync()
    {
        return await CurrentPriceDisplay.InnerTextAsync();
    }

    public async Task<bool> IsSubmitButtonDisabledAsync()
    {
        return await SubmitPriceButton.IsDisabledAsync();
    }

    public async Task<bool> IsPriceEditFormVisibleAsync()
    {
        return await PriceEditForm.IsVisibleAsync();
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

    public async Task<bool> IsOnPageForSkuAsync(string sku)
    {
        var url = _page.Url;
        return url.Contains($"/products/{sku}/price");
    }
}
