namespace VendorPortal.E2ETests.Pages;

/// <summary>
/// Page Object Model for the submit change request page (/change-requests/submit).
/// </summary>
public sealed class SubmitChangeRequestPage(IPage page)
{
    public async Task NavigateAsync()
    {
        // Verify we're on the dashboard before attempting navigation
        var currentUrl = page.Url;
        Console.WriteLine($"DEBUG NavigateAsync: Current URL before click: {currentUrl}");

        // Check if the button exists
        var buttonExists = await page.GetByTestId("submit-change-request-btn").CountAsync() > 0;
        Console.WriteLine($"DEBUG NavigateAsync: submit-change-request-btn exists: {buttonExists}");

        if (!buttonExists)
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
    }

    public async Task FillSkuAsync(string sku)
    {
        // DEBUG: Log page content if sku-field is missing
        var skuFieldExists = await page.GetByTestId("sku-field").CountAsync() > 0;
        if (!skuFieldExists)
        {
            var content = await page.ContentAsync();
            Console.WriteLine($"DEBUG: sku-field not found. Page URL: {page.Url}");
            Console.WriteLine($"DEBUG: Page contains 'Submit Change Request': {content.Contains("Submit Change Request")}");
            Console.WriteLine($"DEBUG: Page contains 'permission': {content.Contains("permission")}");
            Console.WriteLine($"DEBUG: Page contains 'Sign In': {content.Contains("Sign In")}");
        }

        var input = page.GetByTestId("sku-field").Locator("input");
        await input.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await input.FillAsync(sku);
    }

    public async Task FillTitleAsync(string title)
    {
        var input = page.GetByTestId("title-field").Locator("input");
        await input.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await input.FillAsync(title);
    }

    public async Task FillDetailsAsync(string details)
    {
        var textarea = page.GetByTestId("details-field").Locator("textarea").First;
        await textarea.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await textarea.FillAsync(details);
    }

    public async Task ClickSaveDraftAsync() =>
        await page.GetByTestId("save-draft-btn").ClickAsync();

    public async Task ClickSubmitAsync() =>
        await page.GetByTestId("submit-btn").ClickAsync();
}
