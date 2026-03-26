using Microsoft.Playwright;

namespace Backoffice.E2ETests.Pages;

/// <summary>
/// Page Object Model for the Backoffice User List page (SystemAdmin only).
/// Covers user browsing, search filtering, and navigation to user create/edit.
/// </summary>
public sealed class UserListPage
{
    private readonly IPage _page;
    private readonly string _baseUrl;

    public UserListPage(IPage page, string baseUrl)
    {
        _page = page;
        _baseUrl = baseUrl;
    }

    // Locators
    private ILocator UserTable => _page.GetByTestId("user-table");
    private ILocator SearchField => _page.Locator("input[placeholder='Search by email or name...']");
    private ILocator CreateUserButton => _page.GetByTestId("create-user-button");
    private ILocator LoadingIndicator => _page.Locator(".mud-progress-linear");

    // Actions
    public async Task NavigateAsync()
    {
        await _page.GotoAsync($"{_baseUrl}/users", new PageGotoOptions { WaitUntil = WaitUntilState.Commit });

        // Wait for page to be fully loaded
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await UserTable.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
    }

    public async Task SearchForUserAsync(string searchTerm)
    {
        await SearchField.FillAsync(searchTerm);
        // Wait a moment for client-side filtering to complete
        await _page.WaitForTimeoutAsync(500);
    }

    public async Task ClickCreateUserAsync()
    {
        await CreateUserButton.ClickAsync();
        // Wait for navigation to create page
        await _page.WaitForURLAsync(url => url.Contains("/users/create"), new() { Timeout = 10_000, WaitUntil = WaitUntilState.Commit });
    }

    public async Task ClickEditForUserAsync(string email)
    {
        var editButton = _page.GetByTestId($"edit-user-{email}");
        await editButton.ClickAsync();
        // Wait for navigation to edit page
        await _page.WaitForURLAsync(url => url.Contains("/users/") && url.Contains("/edit"), new() { Timeout = 10_000, WaitUntil = WaitUntilState.Commit });
    }

    // Assertions
    public async Task<bool> IsUserVisibleAsync(string email)
    {
        var userEmail = _page.GetByTestId($"user-email-{email}");
        return await userEmail.IsVisibleAsync();
    }

    public async Task<int> GetVisibleUserCountAsync()
    {
        // Count rows in the table (excluding header)
        var rows = _page.Locator("table tbody tr");
        return await rows.CountAsync();
    }

    public async Task<bool> IsUserTableVisibleAsync()
    {
        return await UserTable.IsVisibleAsync();
    }

    public async Task<bool> IsCreateUserButtonVisibleAsync()
    {
        return await CreateUserButton.IsVisibleAsync();
    }

    public async Task<bool> IsLoadingAsync()
    {
        return await LoadingIndicator.IsVisibleAsync();
    }

    public async Task<IReadOnlyList<string>> GetVisibleUserEmailsAsync()
    {
        // Get all email cells (assuming email is in a specific column)
        var emailCells = _page.Locator("table tbody tr td");
        var count = await emailCells.CountAsync();

        var emails = new List<string>();
        for (int i = 0; i < count; i++)
        {
            var text = await emailCells.Nth(i).TextContentAsync();
            if (!string.IsNullOrEmpty(text) && text.Contains("@"))
            {
                emails.Add(text.Trim());
            }
        }

        return emails;
    }
}
