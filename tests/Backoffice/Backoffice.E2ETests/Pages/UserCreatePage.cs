using Microsoft.Playwright;

namespace Backoffice.E2ETests.Pages;

/// <summary>
/// Page Object Model for the Backoffice User Create page (SystemAdmin only).
/// Covers user creation form, validation, and error handling.
/// </summary>
public sealed class UserCreatePage
{
    private readonly IPage _page;
    private readonly string _baseUrl;

    public UserCreatePage(IPage page, string baseUrl)
    {
        _page = page;
        _baseUrl = baseUrl;
    }

    // Locators - Form Fields
    private ILocator EmailInput => _page.GetByTestId("email-input");
    private ILocator PasswordInput => _page.GetByTestId("password-input");
    private ILocator FirstNameInput => _page.GetByTestId("first-name-input");
    private ILocator LastNameInput => _page.GetByTestId("last-name-input");
    private ILocator RoleDropdownWrapper => _page.Locator("[data-testid='role-dropdown']");
    private ILocator RoleDropdownPopover => _page.Locator(".mud-popover-open");

    // Locators - Action Buttons
    private ILocator SubmitButton => _page.GetByTestId("submit-button");
    private ILocator CancelButton => _page.GetByTestId("cancel-button");

    // Locators - Feedback
    private ILocator LoadingIndicator => _page.Locator(".mud-progress-linear");
    private ILocator SuccessMessage => _page.GetByTestId("success-message");
    private ILocator ErrorMessage => _page.GetByTestId("error-message");

    // Actions
    public async Task NavigateAsync()
    {
        await _page.GotoAsync($"{_baseUrl}/users/create", new PageGotoOptions { WaitUntil = WaitUntilState.Commit });

        // Wait for page to be fully loaded
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await EmailInput.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
    }

    public async Task SetEmailAsync(string email)
    {
        await EmailInput.FillAsync(email);
    }

    public async Task SetPasswordAsync(string password)
    {
        await PasswordInput.FillAsync(password);
    }

    public async Task SetFirstNameAsync(string firstName)
    {
        await FirstNameInput.FillAsync(firstName);
    }

    public async Task SetLastNameAsync(string lastName)
    {
        await LastNameInput.FillAsync(lastName);
    }

    public async Task SelectRoleAsync(string roleName)
    {
        // Click the MudSelect wrapper to open dropdown
        await RoleDropdownWrapper.ClickAsync();

        // Wait for popover to appear
        await _page.WaitForTimeoutAsync(300);

        // Click the role option in the popover
        var roleOption = RoleDropdownPopover.Locator($"text={roleName}");
        await roleOption.ClickAsync();

        // Wait for popover to close
        await _page.WaitForTimeoutAsync(300);
    }

    public async Task ClickSubmitAsync()
    {
        await SubmitButton.ClickAsync();
        // Wait for submit operation to complete
        await _page.WaitForTimeoutAsync(1000);
    }

    public async Task ClickCancelAsync()
    {
        await CancelButton.ClickAsync();
        // Wait for navigation back to list
        await _page.WaitForURLAsync(url => url.Contains("/users") && !url.Contains("/create"), new() { Timeout = 10_000 });
    }

    // Assertions - Form State
    public async Task<bool> IsSubmitButtonEnabledAsync()
    {
        return !(await SubmitButton.IsDisabledAsync());
    }

    public async Task<bool> IsSubmitButtonDisabledAsync()
    {
        return await SubmitButton.IsDisabledAsync();
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

    public async Task<bool> IsErrorMessageVisibleAsync()
    {
        return await ErrorMessage.IsVisibleAsync();
    }

    public async Task<string?> GetErrorMessageTextAsync()
    {
        return await ErrorMessage.TextContentAsync();
    }

    public async Task<bool> IsOnCreatePageAsync()
    {
        var currentUrl = _page.Url;
        return currentUrl.Contains("/users/create");
    }
}
