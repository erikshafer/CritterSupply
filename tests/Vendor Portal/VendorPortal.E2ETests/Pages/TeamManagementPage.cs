namespace VendorPortal.E2ETests.Pages;

/// <summary>
/// Page Object Model for the Vendor Portal Team Management page (/team).
/// Uses data-testid attributes for stable element selection.
/// </summary>
public sealed class TeamManagementPage(IPage page)
{
    public async Task NavigateAsync() => await page.GotoAsync("/team");

    // ─── Page Chrome ───

    public ILocator PageTitle => page.GetByTestId("team-page-title");
    public ILocator AdminOnlyMessage => page.GetByTestId("team-admin-only-message");

    // ─── Roster ───

    public ILocator RosterLoading => page.GetByTestId("roster-loading");
    public ILocator RosterErrorMessage => page.GetByTestId("roster-error-message");
    public ILocator RosterRetryButton => page.GetByTestId("roster-retry-btn");
    public ILocator RosterEmptyMessage => page.GetByTestId("roster-empty-message");
    public ILocator RosterTable => page.GetByTestId("team-roster-table");
    public ILocator RosterCount => page.GetByTestId("roster-count");

    // ─── Invitations ───

    public ILocator InvitationsLoading => page.GetByTestId("invitations-loading");
    public ILocator InvitationsErrorMessage => page.GetByTestId("invitations-error-message");
    public ILocator InvitationsRetryButton => page.GetByTestId("invitations-retry-btn");
    public ILocator InvitationsEmptyMessage => page.GetByTestId("invitations-empty-message");
    public ILocator PendingInvitationsTable => page.GetByTestId("pending-invitations-table");
    public ILocator InvitationsCount => page.GetByTestId("invitations-count");

    // ─── Navigation & Loading ───

    /// <summary>
    /// Waits for the team page to finish loading by waiting for either the roster table
    /// or the admin-only message to appear (covers both admin and non-admin flows).
    /// </summary>
    public async Task WaitForLoadedAsync(int timeoutMs = 30000)
    {
        // Wait for the page title first — confirms Blazor has rendered the component
        await PageTitle.WaitForAsync(new LocatorWaitForOptions { Timeout = timeoutMs });

        // Then wait for either the roster table or the admin-only gating message
        await page.WaitForSelectorAsync(
            "[data-testid='team-roster-table'], [data-testid='team-admin-only-message'], [data-testid='roster-empty-message']",
            new PageWaitForSelectorOptions { Timeout = timeoutMs });
    }

    // ─── Roster Queries ───

    public async Task<string> GetMemberCountAsync() =>
        await RosterCount.InnerTextAsync();

    public async Task<bool> IsRosterEmptyAsync() =>
        await RosterEmptyMessage.IsVisibleAsync();

    public async Task<string> GetMemberNameAsync(string userId) =>
        await page.GetByTestId($"member-name-{userId}").InnerTextAsync();

    public async Task<string> GetMemberEmailAsync(string userId) =>
        await page.GetByTestId($"member-email-{userId}").InnerTextAsync();

    public async Task<string> GetMemberRoleAsync(string userId) =>
        await page.GetByTestId($"member-role-{userId}").InnerTextAsync();

    public async Task<string> GetMemberStatusAsync(string userId) =>
        await page.GetByTestId($"member-status-{userId}").InnerTextAsync();

    public async Task<string> GetMemberLastLoginAsync(string userId) =>
        await page.GetByTestId($"member-last-login-{userId}").InnerTextAsync();

    // ─── Admin Gating ───

    public async Task<bool> IsAdminOnlyMessageVisibleAsync() =>
        await AdminOnlyMessage.IsVisibleAsync();

    // ─── Invitations Queries ───

    public async Task<bool> IsPendingInvitationsTableVisibleAsync() =>
        await PendingInvitationsTable.IsVisibleAsync();

    public async Task<string> GetPendingInvitationCountAsync() =>
        await InvitationsCount.InnerTextAsync();

    public async Task<bool> IsInvitationsEmptyAsync() =>
        await InvitationsEmptyMessage.IsVisibleAsync();
}
