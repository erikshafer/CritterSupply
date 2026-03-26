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
        await _page.GotoAsync($"{_baseUrl}/login", new PageGotoOptions { WaitUntil = WaitUntilState.Commit });

        // Wait for login form to be interactive (60s timeout for CI where WASM hydration can take 30s+)
        await EmailInput.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 60_000 });
    }

    public async Task LoginAsync(string email, string password)
    {
        await EmailInput.FillAsync(email);
        await PasswordInput.FillAsync(password);
        await LoginButton.ClickAsync();

        // Wait for either success navigation (dashboard) or error message
        // Use WaitUntil.Commit — Blazor WASM client-side routing doesn't trigger Load events
        await _page.WaitForURLAsync(
            url => url.Contains("/dashboard") || url.Contains("/login"),
            new() { Timeout = 30_000, WaitUntil = WaitUntilState.Commit }
        );
    }

    public async Task LoginAndWaitForDashboardAsync(string email, string password)
    {
        await EmailInput.FillAsync(email);
        await PasswordInput.FillAsync(password);
        await LoginButton.ClickAsync();

        // Wait for successful navigation to dashboard (60s timeout for CI WASM bootstrap)
        // Use WaitUntil.Commit — Blazor WASM client-side routing doesn't trigger Load events
        await _page.WaitForURLAsync(url => url.Contains("/dashboard"), new() { Timeout = 60_000, WaitUntil = WaitUntilState.Commit });
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
