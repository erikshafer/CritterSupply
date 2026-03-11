namespace VendorPortal.E2ETests.Pages;

/// <summary>
/// Page Object Model for the Vendor Portal login page (/login).
/// Uses data-testid attributes for stable element selection.
/// </summary>
public sealed class VendorLoginPage(IPage page)
{
    public async Task NavigateAsync() => await page.GotoAsync("/login");

    public async Task FillEmailAsync(string email) =>
        await page.GetByTestId("email-field").Locator("input").FillAsync(email);

    public async Task FillPasswordAsync(string password) =>
        await page.GetByTestId("password-field").Locator("input").FillAsync(password);

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
