namespace Storefront.E2ETests.Pages;

/// <summary>
/// Page Object Model for the Login page (/account/login).
///
/// Uses ARIA roles and semantic selectors over CSS class names.
/// MudBlazor classes change with version bumps; ARIA roles and data-testid attributes do not.
/// </summary>
public sealed class LoginPage(IPage page)
{
    private ILocator EmailInput => page.GetByLabel("Email");
    private ILocator PasswordInput => page.GetByLabel("Password");
    private ILocator LoginButton => page.GetByRole(AriaRole.Button, new() { Name = "Sign In" });
    private ILocator ErrorMessage => page.GetByRole(AriaRole.Alert);

    public async Task NavigateAsync()
    {
        await page.GotoAsync("/account/login");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    public async Task LoginAsync(string email, string password)
    {
        await EmailInput.FillAsync(email);
        await PasswordInput.FillAsync(password);
        await LoginButton.ClickAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    public async Task LoginAsAliceAsync()
    {
        await LoginAsync(
            WellKnownTestData.Customers.AliceEmail,
            WellKnownTestData.Customers.AlicePassword);
    }

    public async Task<bool> IsLoggedInAsync()
    {
        // After successful login, the user is redirected away from /account/login
        return !page.Url.Contains("/account/login");
    }

    public async Task<string> GetErrorMessageAsync()
    {
        await ErrorMessage.WaitForAsync(new() { Timeout = 5000 });
        return await ErrorMessage.InnerTextAsync();
    }
}
