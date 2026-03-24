namespace VendorPortal.E2ETests.Pages;

/// <summary>
/// Page Object Model for the submit change request page (/change-requests/submit).
/// </summary>
public sealed class SubmitChangeRequestPage(IPage page)
{
    public async Task NavigateAsync()
    {
        // CRITICAL: Ensure no Blazor error UI is present before navigation.
        // Error UI appears when SignalR hub connection fails during Dashboard initialization.
        // This check prevents navigation attempts while the error overlay is blocking interactions.
        var errorUI = page.Locator("#blazor-error-ui");
        var isErrorVisible = await errorUI.IsVisibleAsync();
        if (isErrorVisible)
        {
            throw new InvalidOperationException(
                "Blazor error UI is visible before navigation to submit page. " +
                "This indicates an unhandled exception (likely SignalR hub connection failure) " +
                "occurred during Dashboard initialization. Check browser console logs.");
        }

        // Verify we're on the dashboard before attempting navigation
        var currentUrl = page.Url;
        Console.WriteLine($"DEBUG NavigateAsync: Current URL before click: {currentUrl}");

        // Check if the button exists (with 60s timeout for CI environments)
        var button = page.GetByTestId("submit-change-request-btn");
        await button.WaitForAsync(new LocatorWaitForOptions
        {
            Timeout = 60000,
            State = WaitForSelectorState.Visible
        });

        var buttonCount = await button.CountAsync();
        Console.WriteLine($"DEBUG NavigateAsync: submit-change-request-btn count: {buttonCount}");

        if (buttonCount == 0)
        {
            throw new InvalidOperationException(
                "submit-change-request-btn not found on page. User may not have permission or not on Dashboard.");
        }

        // Use Blazor SPA navigation by clicking the dashboard link
        // (GotoAsync would reload the app and lose in-memory auth state)
        await page.GetByTestId("submit-change-request-btn").ClickAsync();

        Console.WriteLine("DEBUG NavigateAsync: Button clicked");

        // Wait for client-side navigation to complete
        await page.WaitForURLAsync("**/change-requests/submit", new PageWaitForURLOptions
        {
            WaitUntil = WaitUntilState.Commit,
            Timeout = 15000
        });

        Console.WriteLine($"DEBUG NavigateAsync: URL after navigation: {page.Url}");

        // Wait for NetworkIdle to ensure Blazor components are hydrated
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions
        {
            Timeout = 15000
        });

        Console.WriteLine("DEBUG NavigateAsync: NetworkIdle reached");

        // CRITICAL: Wait for the SKU field to be ready before completing navigation.
        // This ensures the form is fully rendered and interactive.
        await page.GetByTestId("sku-field").WaitForAsync(new LocatorWaitForOptions
        {
            Timeout = 15000,
            State = WaitForSelectorState.Visible
        });

        Console.WriteLine("DEBUG NavigateAsync: SKU field is visible and ready");
    }

    public async Task FillSkuAsync(string sku)
    {
        // data-testid is on the input element itself, not a container
        var input = page.GetByTestId("sku-field");
        await input.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await input.FillAsync(sku);
    }

    public async Task FillTitleAsync(string title)
    {
        // data-testid is on the input element itself, not a container
        var input = page.GetByTestId("title-field");
        await input.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await input.FillAsync(title);
    }

    public async Task FillDetailsAsync(string details)
    {
        // data-testid is on the input element itself (MudBlazor renders textarea as input)
        var input = page.GetByTestId("details-field");
        await input.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await input.FillAsync(details);
    }

    public async Task ClickSaveDraftAsync() =>
        await page.GetByTestId("save-draft-btn").ClickAsync();

    public async Task ClickSubmitAsync() =>
        await page.GetByTestId("submit-btn").ClickAsync();
}
