namespace Backoffice.E2ETests.Pages;

/// <summary>
/// Page Object Model for the Backoffice Operations Alerts Feed.
/// Covers low-stock alerts, acknowledgment workflow, and real-time SignalR push notifications.
/// </summary>
public sealed class OperationsAlertsPage
{
    private readonly IPage _page;
    private readonly string _baseUrl;

    public OperationsAlertsPage(IPage page, string baseUrl)
    {
        _page = page;
        _baseUrl = baseUrl;
    }

    // Locators - Alert Feed
    private ILocator AlertFeed => _page.GetByTestId("operations-alerts-feed");
    private ILocator AlertRows => AlertFeed.Locator("[data-testid^='alert-']");
    private ILocator NoAlertsMessage => _page.GetByTestId("no-alerts-message");
    private ILocator LastUpdatedTimestamp => _page.GetByTestId("alerts-last-updated");
    private ILocator RefreshButton => _page.GetByTestId("refresh-alerts-btn");
    private ILocator NewAlertsBanner => _page.GetByTestId("new-alerts-banner");

    // Locators - Filters (MudBlazor MudSelect pattern)
    private ILocator SeverityFilter => _page.GetByTestId("alert-severity-filter");
    private ILocator StatusFilter => _page.GetByTestId("alert-status-filter");

    // Locators - Real-time indicator
    private ILocator RealtimeIndicator => _page.GetByTestId("alerts-realtime-connected");
    private ILocator RealtimeDisconnected => _page.GetByTestId("alerts-realtime-disconnected");

    // Locators - Alert Details Modal
    private ILocator AlertDetailsModal => _page.GetByTestId("alert-details-modal");
    private ILocator AlertDetailsTitle => AlertDetailsModal.Locator("[data-testid='alert-title']");
    private ILocator AlertDetailsMessage => AlertDetailsModal.Locator("[data-testid='alert-message']");
    private ILocator AlertDetailsSeverity => AlertDetailsModal.Locator("[data-testid='alert-severity']");
    private ILocator AcknowledgeButton => AlertDetailsModal.GetByTestId("alert-acknowledge-button");
    private ILocator CloseModalButton => AlertDetailsModal.GetByTestId("alert-close-button");

    // Actions - Navigation
    public async Task NavigateAsync()
    {
        await _page.GotoAsync($"{_baseUrl}/alerts", new PageGotoOptions { WaitUntil = WaitUntilState.Commit });

        // Wait for alerts feed to be fully loaded
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await AlertFeed.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
    }

    public async Task WaitForRealtimeConnectionAsync()
    {
        // Wait for SignalR connection indicator to show "Connected"
        await RealtimeIndicator.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
    }

    // Actions - Filtering (MudBlazor MudSelect interaction pattern from e2e-playwright-testing.md)
    public async Task FilterBySeverityAsync(string severity)
    {
        // Click MudSelect to open dropdown
        await SeverityFilter.ClickAsync();

        // Wait for dropdown list to appear
        var dropdown = _page.Locator(".mud-popover-open .mud-list");
        await dropdown.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 3_000 });

        // Click the desired option by text
        var option = dropdown.Locator($"text={severity}");
        await option.ClickAsync();

        // Wait for dropdown to close and alerts to refresh
        await dropdown.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 3_000 });
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    public async Task FilterByStatusAsync(string status)
    {
        // Click MudSelect to open dropdown
        await StatusFilter.ClickAsync();

        // Wait for dropdown list to appear
        var dropdown = _page.Locator(".mud-popover-open .mud-list");
        await dropdown.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 3_000 });

        // Click the desired option by text
        var option = dropdown.Locator($"text={status}");
        await option.ClickAsync();

        // Wait for dropdown to close and alerts to refresh
        await dropdown.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 3_000 });
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    // Actions - Alert Interaction
    public async Task ClickAlertAsync(Guid alertId)
    {
        var alertRow = _page.GetByTestId($"alert-{alertId}");
        await alertRow.ClickAsync();

        // Wait for alert details modal to appear
        await AlertDetailsModal.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
    }

    public async Task AcknowledgeAlertAsync(Guid alertId)
    {
        // Click alert to open details modal
        await ClickAlertAsync(alertId);

        // Click acknowledge button
        await AcknowledgeButton.ClickAsync();

        // Wait for modal to close or acknowledgment confirmation
        await AlertDetailsModal.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 5_000 });
    }

    public async Task CloseAlertDetailsModalAsync()
    {
        await CloseModalButton.ClickAsync();
        await AlertDetailsModal.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 3_000 });
    }

    // Assertions - Alert Feed
    public async Task<int> GetAlertCountAsync()
    {
        try
        {
            await AlertFeed.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 2_000 });
            return await AlertRows.CountAsync();
        }
        catch (TimeoutException)
        {
            return 0;
        }
    }

    public async Task<bool> IsNoAlertsMessageVisibleAsync()
    {
        try
        {
            await NoAlertsMessage.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 2_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<Guid>> GetAlertIdsAsync()
    {
        var alertIds = new List<Guid>();
        var count = await GetAlertCountAsync();

        for (int i = 0; i < count; i++)
        {
            var row = AlertRows.Nth(i);
            var testId = await row.GetAttributeAsync("data-testid");
            if (testId != null && testId.StartsWith("alert-"))
            {
                var idStr = testId.Replace("alert-", "");
                if (Guid.TryParse(idStr, out var alertId))
                {
                    alertIds.Add(alertId);
                }
            }
        }

        return alertIds;
    }

    public async Task<IReadOnlyList<string>> GetAlertSeveritiesAsync()
    {
        var severities = new List<string>();
        var count = await GetAlertCountAsync();

        for (int i = 0; i < count; i++)
        {
            var row = AlertRows.Nth(i);
            var severityCell = row.Locator("[data-testid='alert-severity']");
            var severity = await severityCell.TextContentAsync();
            if (!string.IsNullOrWhiteSpace(severity))
            {
                severities.Add(severity.Trim());
            }
        }

        return severities;
    }

    public async Task<IReadOnlyList<string>> GetAlertStatusesAsync()
    {
        var statuses = new List<string>();
        var count = await GetAlertCountAsync();

        for (int i = 0; i < count; i++)
        {
            var row = AlertRows.Nth(i);
            var statusCell = row.Locator("[data-testid='alert-status']");
            var status = await statusCell.TextContentAsync();
            if (!string.IsNullOrWhiteSpace(status))
            {
                statuses.Add(status.Trim());
            }
        }

        return statuses;
    }

    // Assertions - Alert Details Modal
    public async Task<bool> IsAlertDetailsModalVisibleAsync()
    {
        try
        {
            await AlertDetailsModal.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 2_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    public async Task<string?> GetAlertDetailsTitleAsync()
    {
        if (await IsAlertDetailsModalVisibleAsync())
        {
            return await AlertDetailsTitle.TextContentAsync();
        }
        return null;
    }

    public async Task<string?> GetAlertDetailsMessageAsync()
    {
        if (await IsAlertDetailsModalVisibleAsync())
        {
            return await AlertDetailsMessage.TextContentAsync();
        }
        return null;
    }

    public async Task<string?> GetAlertDetailsSeverityAsync()
    {
        if (await IsAlertDetailsModalVisibleAsync())
        {
            return await AlertDetailsSeverity.TextContentAsync();
        }
        return null;
    }

    public async Task<bool> IsAcknowledgeButtonVisibleAsync()
    {
        try
        {
            await AcknowledgeButton.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 2_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    // Assertions - Real-time
    public async Task<bool> IsRealtimeConnectedAsync()
    {
        try
        {
            await RealtimeIndicator.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 2_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    public async Task<bool> IsRealtimeDisconnectedAsync()
    {
        try
        {
            await RealtimeDisconnected.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 2_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    public async Task<bool> IsOnOperationsAlertsPageAsync()
    {
        return _page.Url.Contains("/alerts");
    }

    // Wait for new alert to appear (for real-time SignalR push scenarios)
    public async Task WaitForNewAlertAsync(Guid alertId, int timeoutMs = 5_000)
    {
        var alertRow = _page.GetByTestId($"alert-{alertId}");

        // Poll until alert row appears (SignalR push may take 100-500ms)
        var endTime = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < endTime)
        {
            try
            {
                await alertRow.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 500 });
                return; // Alert appeared
            }
            catch (TimeoutException)
            {
                // Continue polling
            }
        }

        throw new TimeoutException($"Alert '{alertId}' did not appear within {timeoutMs}ms");
    }

    // Wait for alert status change (for acknowledgment workflow)
    public async Task WaitForAlertStatusChangeAsync(Guid alertId, string expectedStatus, int timeoutMs = 5_000)
    {
        var alertRow = _page.GetByTestId($"alert-{alertId}");
        var statusCell = alertRow.Locator("[data-testid='alert-status']");

        await statusCell.WaitForAsync(new() { State = WaitForSelectorState.Visible });

        // Poll until status matches
        var endTime = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < endTime)
        {
            var currentStatus = await statusCell.TextContentAsync();
            if (currentStatus?.Trim() == expectedStatus)
            {
                return;
            }
            await Task.Delay(100);
        }

        throw new TimeoutException($"Alert '{alertId}' status did not change to '{expectedStatus}' within {timeoutMs}ms");
    }

    // P1-2: Data Freshness Indicators
    public async Task<string?> GetLastUpdatedTimestampAsync()
    {
        try
        {
            await LastUpdatedTimestamp.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 2_000 });
            return await LastUpdatedTimestamp.TextContentAsync();
        }
        catch (TimeoutException)
        {
            return null;
        }
    }

    public async Task<bool> IsLastUpdatedTimestampVisibleAsync()
    {
        try
        {
            await LastUpdatedTimestamp.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 2_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    public async Task ClickRefreshButtonAsync()
    {
        await RefreshButton.ClickAsync();
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    // P1-2: New Alerts Banner
    public async Task<bool> IsNewAlertsBannerVisibleAsync()
    {
        try
        {
            await NewAlertsBanner.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 2_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    public async Task<string?> GetNewAlertsBannerTextAsync()
    {
        if (await IsNewAlertsBannerVisibleAsync())
        {
            return await NewAlertsBanner.TextContentAsync();
        }
        return null;
    }

    public async Task ClickRefreshButtonInBannerAsync()
    {
        // The banner has a "Refresh" button inside it
        var refreshButtonInBanner = NewAlertsBanner.Locator("button:has-text('Refresh')");
        await refreshButtonInBanner.ClickAsync();
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    public async Task<bool> IsBannerHiddenAsync()
    {
        try
        {
            await NewAlertsBanner.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 2_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    // SessionExpirySteps and AuthorizationSteps support methods
    public async Task<bool> IsAlertFeedVisibleAsync()
    {
        try
        {
            await AlertFeed.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 2_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    public async Task ClickFirstAlertAsync()
    {
        var firstAlert = AlertRows.First;
        await firstAlert.ClickAsync();
        await AlertDetailsModal.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
    }

    public async Task ClickAlertBySku(string sku)
    {
        // Find alert row by SKU text content
        var alertRow = AlertRows.Filter(new() { HasText = sku }).First;
        await alertRow.ClickAsync();
        await AlertDetailsModal.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
    }

    public async Task FilterByStatus(string status)
    {
        await FilterByStatusAsync(status);
    }

    public async Task AcknowledgeAlertAsync()
    {
        // Acknowledge currently open alert in modal
        await AcknowledgeButton.ClickAsync();
        await AlertDetailsModal.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 5_000 });
    }
}
