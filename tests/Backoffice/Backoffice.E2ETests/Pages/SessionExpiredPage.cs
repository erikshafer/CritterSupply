using Microsoft.Playwright;

namespace Backoffice.E2ETests.Pages;

/// <summary>
/// Page Object Model for the Session Expired Modal.
/// Covers the blocking modal that appears when JWT tokens expire (401 Unauthorized responses).
/// </summary>
public sealed class SessionExpiredPage
{
    private readonly IPage _page;

    public SessionExpiredPage(IPage page)
    {
        _page = page;
    }

    // Locators - Session Expired Modal
    private ILocator SessionExpiredModal => _page.GetByTestId("session-expired-modal");
    private ILocator ModalTitle => SessionExpiredModal.Locator("[data-testid='modal-title']");
    private ILocator ModalMessage => SessionExpiredModal.Locator("[data-testid='modal-message']");
    private ILocator LogInAgainButton => SessionExpiredModal.GetByTestId("session-expired-login-button");
    private ILocator CloseModalButton => SessionExpiredModal.GetByTestId("session-expired-close-button");

    // Assertions - Modal Visibility
    public async Task<bool> IsSessionExpiredModalVisibleAsync()
    {
        try
        {
            await SessionExpiredModal.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    public async Task<bool> IsModalHiddenAsync()
    {
        try
        {
            await SessionExpiredModal.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 3_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    // Assertions - Modal Content
    public async Task<string?> GetModalTitleAsync()
    {
        if (await IsSessionExpiredModalVisibleAsync())
        {
            return await ModalTitle.TextContentAsync();
        }
        return null;
    }

    public async Task<string?> GetModalMessageAsync()
    {
        if (await IsSessionExpiredModalVisibleAsync())
        {
            return await ModalMessage.TextContentAsync();
        }
        return null;
    }

    // Actions - Modal Interaction
    public async Task ClickLogInAgainAsync()
    {
        await LogInAgainButton.ClickAsync();

        // Wait for navigation to login page
        await _page.WaitForURLAsync(url => url.Contains("/login"), new() { Timeout = 5_000 });
    }

    public async Task CloseModalAsync()
    {
        await CloseModalButton.ClickAsync();

        // Wait for modal to close
        await SessionExpiredModal.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 3_000 });
    }

    // Assertions - Button Visibility
    public async Task<bool> IsLogInAgainButtonVisibleAsync()
    {
        try
        {
            await LogInAgainButton.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 2_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    // Assertions - Modal Blocking Behavior
    public async Task<bool> CanInteractWithPageBehindModalAsync()
    {
        // Attempt to click a specific element behind the modal (e.g., a button on the dashboard)
        // If modal is blocking correctly, this should fail or be intercepted
        try
        {
            var backgroundElement = _page.GetByTestId("dashboard-refresh-btn");
            await backgroundElement.ClickAsync(new() { Timeout = 1_000, NoWaitAfter = true });
            return true; // If click succeeds, modal is NOT blocking
        }
        catch
        {
            return false; // Modal is blocking correctly
        }
    }

    // Helpers - Wait for modal to appear
    public async Task WaitForSessionExpiredModalAsync(int timeoutMs = 5_000)
    {
        await SessionExpiredModal.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = timeoutMs });
    }

    // Helpers - Count modals (for duplicate modal detection)
    public async Task<int> GetSessionExpiredModalCountAsync()
    {
        var modals = _page.Locator("[data-testid='session-expired-modal']");
        return await modals.CountAsync();
    }
}
