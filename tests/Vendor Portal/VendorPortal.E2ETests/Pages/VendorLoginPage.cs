namespace VendorPortal.E2ETests.Pages;

/// <summary>
/// Page Object Model for the Vendor Portal login page (/login).
/// Uses data-testid attributes for stable element selection.
/// </summary>
public sealed class VendorLoginPage(IPage page)
{
    public async Task NavigateAsync() => await page.GotoAsync("/login");

    public async Task FillEmailAsync(string email)
    {
        // Wait for WASM hydration to complete (NetworkIdle ensures Blazor runtime is loaded)
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Wait for email field to be visible and ready (MudBlazor takes time to render nested inputs)
        // Use 30s timeout for CI environments where WASM startup can be slower
        var emailField = page.GetByTestId("email-field").Locator("input");
        await emailField.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await emailField.FillAsync(email);
    }

    public async Task FillPasswordAsync(string password)
    {
        // Wait for password field to be visible and ready
        // Use 30s timeout for CI environments where WASM startup can be slower
        var passwordField = page.GetByTestId("password-field").Locator("input");
        await passwordField.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await passwordField.FillAsync(password);
    }

    public async Task ClickSignInAsync() =>
        await page.GetByTestId("login-btn").ClickAsync();

    public async Task<bool> IsErrorAlertVisibleAsync() =>
        await page.GetByTestId("login-error-alert").IsVisibleAsync();

    public async Task<string> GetErrorMessageAsync() =>
        await page.GetByTestId("login-error-alert").InnerTextAsync();

    /// <summary>
    /// Complete login flow: navigate → fill credentials → click sign in → wait for navigation.
    /// </summary>
    public async Task LoginAsync(string email, string password)
    {
        await NavigateAsync();
        await FillEmailAsync(email);
        await FillPasswordAsync(password);
        await ClickSignInAsync();
    }
}
