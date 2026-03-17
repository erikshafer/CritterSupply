using Microsoft.Playwright;

namespace Backoffice.E2ETests.Pages;

/// <summary>
/// Page Object Model for the Backoffice authentication flow.
/// Covers JWT login via BackofficeIdentity.Api.
/// </summary>
public sealed class LoginPage
{
    private readonly IPage _page;
    private readonly string _baseUrl;

    public LoginPage(IPage page, string baseUrl)
    {
        _page = page;
        _baseUrl = baseUrl;
    }

    // Locators
    private ILocator EmailInput => _page.GetByTestId("login-email");
    private ILocator PasswordInput => _page.GetByTestId("login-password");
    private ILocator LoginButton => _page.GetByTestId("login-submit");
    private ILocator ErrorMessage => _page.GetByTestId("login-error");
    private ILocator LoadingSpinner => _page.GetByTestId("login-loading");

    // Actions
    public async Task NavigateAsync()
    {
        await _page.GotoAsync($"{_baseUrl}/login");

        // Wait for WASM hydration (5-30s cold start)
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Wait for login form to be interactive
        await EmailInput.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
    }

    public async Task LoginAsync(string email, string password)
    {
        await EmailInput.FillAsync(email);
        await PasswordInput.FillAsync(password);
        await LoginButton.ClickAsync();

        // Wait for either success navigation (dashboard) or error message
        await _page.WaitForURLAsync(
            url => url.Contains("/dashboard") || url.Contains("/login"),
            new() { Timeout = 10_000 }
        );
    }

    public async Task LoginAndWaitForDashboardAsync(string email, string password)
    {
        await EmailInput.FillAsync(email);
        await PasswordInput.FillAsync(password);
        await LoginButton.ClickAsync();

        // Wait for successful navigation to dashboard
        await _page.WaitForURLAsync(url => url.Contains("/dashboard"), new() { Timeout = 10_000 });

        // Wait for dashboard to be fully loaded (MudBlazor hydration)
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    // Assertions
    public async Task<bool> IsErrorVisibleAsync()
    {
        try
        {
            await ErrorMessage.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 2_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    public async Task<string?> GetErrorTextAsync()
    {
        if (await IsErrorVisibleAsync())
        {
            return await ErrorMessage.TextContentAsync();
        }
        return null;
    }

    public async Task<bool> IsOnLoginPageAsync()
    {
        return _page.Url.Contains("/login");
    }

    public async Task<bool> IsLoadingAsync()
    {
        try
        {
            await LoadingSpinner.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 1_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }
}
