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
        // IMPORTANT: Do NOT wait for NetworkIdle here - caller has already verified WASM hydration
        // by waiting for login-btn. NetworkIdle may never complete in TestContainers environment
        // due to background Marten/Wolverine/SignalR network activity.

        // MudBlazor renders data-testid directly on the <input> element (not on a wrapper)
        // Use 60s timeout for CI environments where MudBlazor component initialization can be slow
        var emailField = page.GetByTestId("email-field");
        await emailField.WaitForAsync(new LocatorWaitForOptions { Timeout = 60000 });
        await emailField.FillAsync(email);
    }

    public async Task FillPasswordAsync(string password)
    {
        // MudBlazor renders data-testid directly on the <input> element (not on a wrapper)
        // (No NetworkIdle wait needed - form is already hydrated)
        var passwordField = page.GetByTestId("password-field");
        await passwordField.WaitForAsync(new LocatorWaitForOptions { Timeout = 60000 });
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
