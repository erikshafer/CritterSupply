namespace VendorPortal.E2ETests.Pages;

/// <summary>
/// Page Object Model for the change requests list page (/change-requests).
/// </summary>
public sealed class ChangeRequestsPage(IPage page)
{
    public async Task NavigateAsync()
    {
        // Use Blazor SPA navigation by clicking the dashboard link
        // (GotoAsync would reload the app and lose in-memory auth state)
        await page.GetByTestId("view-change-requests-btn").ClickAsync();

        // Wait for client-side navigation to complete
        await page.WaitForURLAsync("**/change-requests", new PageWaitForURLOptions
        {
            WaitUntil = WaitUntilState.Commit,
            Timeout = 15000
        });
    }

    public ILocator SubmitChangeRequestButton => page.GetByTestId("submit-change-request-btn");
    public ILocator NoRequestsMessage => page.GetByTestId("no-requests-message");
    public ILocator ChangeRequestsTable => page.GetByTestId("change-requests-table");

    public async Task<bool> IsTableVisibleAsync() =>
        await ChangeRequestsTable.IsVisibleAsync();

    public async Task<bool> IsEmptyMessageVisibleAsync() =>
        await NoRequestsMessage.IsVisibleAsync();
}
