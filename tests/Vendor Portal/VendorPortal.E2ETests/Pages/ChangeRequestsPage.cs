namespace VendorPortal.E2ETests.Pages;

/// <summary>
/// Page Object Model for the change requests list page (/change-requests).
/// </summary>
public sealed class ChangeRequestsPage(IPage page)
{
    public async Task NavigateAsync() => await page.GotoAsync("/change-requests");

    public ILocator SubmitChangeRequestButton => page.GetByTestId("submit-change-request-btn");
    public ILocator NoRequestsMessage => page.GetByTestId("no-requests-message");
    public ILocator ChangeRequestsTable => page.GetByTestId("change-requests-table");

    public async Task<bool> IsTableVisibleAsync() =>
        await ChangeRequestsTable.IsVisibleAsync();

    public async Task<bool> IsEmptyMessageVisibleAsync() =>
        await NoRequestsMessage.IsVisibleAsync();
}
