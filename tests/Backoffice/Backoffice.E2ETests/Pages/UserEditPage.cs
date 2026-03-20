using Microsoft.Playwright;

namespace Backoffice.E2ETests.Pages;

/// <summary>
/// Page Object Model for the Backoffice User Edit page (SystemAdmin only).
/// Covers three sections: Change Role, Reset Password, and Deactivate User.
/// Each section uses its own submit button and two-click confirmation patterns.
/// </summary>
public sealed class UserEditPage
{
    private readonly IPage _page;
    private readonly string _baseUrl;

    public UserEditPage(IPage page, string baseUrl)
    {
        _page = page;
        _baseUrl = baseUrl;
    }

    // Locators - Change Role Section
    private ILocator RoleDropdownWrapper => _page.Locator("[data-testid='role-dropdown']");
    private ILocator RoleDropdownPopover => _page.Locator(".mud-popover-open");
    private ILocator ChangeRoleButton => _page.GetByTestId("change-role-button");
    private ILocator RoleSuccessMessage => _page.GetByTestId("role-success-message");

    // Locators - Reset Password Section
    private ILocator NewPasswordInput => _page.GetByTestId("new-password-input");
    private ILocator ConfirmPasswordInput => _page.GetByTestId("confirm-password-input");
    private ILocator ResetPasswordButton => _page.GetByTestId("reset-password-button");
    private ILocator ConfirmResetPasswordButton => _page.GetByTestId("confirm-reset-password-button");
    private ILocator CancelResetButton => _page.GetByTestId("cancel-reset-button");
    private ILocator PasswordSuccessMessage => _page.GetByTestId("password-success-message");

    // Locators - Deactivate User Section
    private ILocator DeactivateSection => _page.GetByTestId("deactivate-section");
    private ILocator DeactivationReasonInput => _page.GetByTestId("deactivation-reason-input");
    private ILocator DeactivateButton => _page.GetByTestId("deactivate-button");
    private ILocator ConfirmDeactivateButton => _page.GetByTestId("confirm-deactivate-button");
    private ILocator CancelDeactivateButton => _page.GetByTestId("cancel-deactivate-button");
    private ILocator DeactivateSuccessMessage => _page.GetByTestId("deactivate-success-message");

    // Locators - Page Elements
    private ILocator LoadingIndicator => _page.Locator(".mud-progress-linear");

    // Actions - Navigation
    public async Task NavigateAsync(Guid userId)
    {
        await _page.GotoAsync($"{_baseUrl}/users/{userId}/edit");

        // Wait for page to be fully loaded
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await RoleDropdownWrapper.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
    }

    // Actions - Change Role
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

    public async Task ClickChangeRoleAsync()
    {
        await ChangeRoleButton.ClickAsync();
        // Wait for operation to complete
        await _page.WaitForTimeoutAsync(1000);
    }

    // Actions - Reset Password
    public async Task SetNewPasswordAsync(string password)
    {
        await NewPasswordInput.FillAsync(password);
    }

    public async Task SetConfirmPasswordAsync(string password)
    {
        await ConfirmPasswordInput.FillAsync(password);
    }

    public async Task ClickResetPasswordAsync()
    {
        await ResetPasswordButton.ClickAsync();
        // Wait for confirmation button to appear
        await _page.WaitForTimeoutAsync(300);
    }

    public async Task ClickConfirmResetPasswordAsync()
    {
        await ConfirmResetPasswordButton.ClickAsync();
        // Wait for operation to complete
        await _page.WaitForTimeoutAsync(1000);
    }

    public async Task ClickCancelResetAsync()
    {
        await CancelResetButton.ClickAsync();
        // Wait for buttons to revert
        await _page.WaitForTimeoutAsync(300);
    }

    // Actions - Deactivate User
    public async Task SetDeactivationReasonAsync(string reason)
    {
        await DeactivationReasonInput.FillAsync(reason);
    }

    public async Task ClickDeactivateAsync()
    {
        await DeactivateButton.ClickAsync();
        // Wait for confirmation button to appear
        await _page.WaitForTimeoutAsync(300);
    }

    public async Task ClickConfirmDeactivateAsync()
    {
        await ConfirmDeactivateButton.ClickAsync();
        // Wait for operation to complete
        await _page.WaitForTimeoutAsync(1000);
    }

    public async Task ClickCancelDeactivateAsync()
    {
        await CancelDeactivateButton.ClickAsync();
        // Wait for buttons to revert
        await _page.WaitForTimeoutAsync(300);
    }

    // Assertions - Change Role
    public async Task<bool> IsRoleSuccessMessageVisibleAsync()
    {
        return await RoleSuccessMessage.IsVisibleAsync();
    }

    public async Task<string?> GetRoleSuccessMessageTextAsync()
    {
        return await RoleSuccessMessage.TextContentAsync();
    }

    public async Task<bool> IsChangeRoleButtonDisabledAsync()
    {
        return await ChangeRoleButton.IsDisabledAsync();
    }

    // Assertions - Reset Password
    public async Task<bool> IsResetPasswordButtonDisabledAsync()
    {
        return await ResetPasswordButton.IsDisabledAsync();
    }

    public async Task<bool> IsConfirmResetPasswordButtonVisibleAsync()
    {
        return await ConfirmResetPasswordButton.IsVisibleAsync();
    }

    public async Task<bool> IsCancelResetButtonVisibleAsync()
    {
        return await CancelResetButton.IsVisibleAsync();
    }

    public async Task<bool> IsPasswordSuccessMessageVisibleAsync()
    {
        return await PasswordSuccessMessage.IsVisibleAsync();
    }

    public async Task<string?> GetPasswordSuccessMessageTextAsync()
    {
        return await PasswordSuccessMessage.TextContentAsync();
    }

    // Assertions - Deactivate User
    public async Task<bool> IsDeactivateSectionVisibleAsync()
    {
        return await DeactivateSection.IsVisibleAsync();
    }

    public async Task<bool> IsDeactivateButtonDisabledAsync()
    {
        return await DeactivateButton.IsDisabledAsync();
    }

    public async Task<bool> IsConfirmDeactivateButtonVisibleAsync()
    {
        return await ConfirmDeactivateButton.IsVisibleAsync();
    }

    public async Task<bool> IsCancelDeactivateButtonVisibleAsync()
    {
        return await CancelDeactivateButton.IsVisibleAsync();
    }

    public async Task<bool> IsDeactivateSuccessMessageVisibleAsync()
    {
        return await DeactivateSuccessMessage.IsVisibleAsync();
    }

    public async Task<string?> GetDeactivateSuccessMessageTextAsync()
    {
        return await DeactivateSuccessMessage.TextContentAsync();
    }

    // Assertions - Page State
    public async Task<bool> IsOnEditPageForUserAsync(Guid userId)
    {
        var currentUrl = _page.Url;
        return currentUrl.Contains($"/users/{userId}/edit");
    }

    public async Task<bool> IsLoadingAsync()
    {
        return await LoadingIndicator.IsVisibleAsync();
    }
}
