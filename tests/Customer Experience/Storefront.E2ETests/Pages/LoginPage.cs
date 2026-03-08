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
        await page.GotoAsync("/login");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    public async Task LoginAsync(string email, string password)
    {
        await EmailInput.FillAsync(email);
        await PasswordInput.FillAsync(password);
        await LoginButton.ClickAsync();

        // forceLoad: true in Login.razor's Navigation.NavigateTo fires a SECOND full-page navigation
        // after the fetch completes. WaitForLoadStateAsync(NetworkIdle) can return between the fetch
        // completing and the hard reload starting — meaning page.Url is still "/login" when read.
        // WaitForURL waits for the navigation away from /login to be fully committed first.
        // Timeout increased to 30s for slower CI environments (cold start, shared resources).
        try
        {
            await page.WaitForURLAsync(
                url => !url.Contains("/login"),
                new() { Timeout = 30_000 });
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        }
        catch (TimeoutException)
        {
            // Login may have failed — page stayed at /login.
            // IsLoggedInAsync() will surface the assertion failure.
        }
    }

    public async Task LoginAsAliceAsync()
    {
        await LoginAsync(
            WellKnownTestData.Customers.AliceEmail,
            WellKnownTestData.Customers.AlicePassword);
    }

    public async Task<bool> IsLoggedInAsync()
    {
        // After successful login, the user is redirected away from /login
        return !page.Url.Contains("/login");
    }

    public async Task<string> GetErrorMessageAsync()
    {
        await ErrorMessage.WaitForAsync(new() { Timeout = 5000 });
        return await ErrorMessage.InnerTextAsync();
    }
}
