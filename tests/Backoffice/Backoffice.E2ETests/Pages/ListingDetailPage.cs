namespace Backoffice.E2ETests.Pages;

/// <summary>
/// Page Object Model for the Backoffice Listing Detail page.
/// Route: /admin/listings/{id}
/// Read-only detail view showing listing state, content, metadata, and disabled action stubs.
/// </summary>
public sealed class ListingDetailPage
{
    private readonly IPage _page;
    private readonly string _baseUrl;

    private const int WasmBootstrapTimeoutMs = 60_000;
    private const int ApiCallTimeoutMs = 15_000;

    public ListingDetailPage(IPage page, string baseUrl)
    {
        _page = page;
        _baseUrl = baseUrl;
    }

    // Locators — match ListingDetail.razor data-testid attributes
    private ILocator PageContainer => _page.GetByTestId("listing-detail-page");
    private ILocator ListingId => _page.GetByTestId("listing-id");
    private ILocator ListingSku => _page.GetByTestId("listing-sku");
    private ILocator ListingChannel => _page.GetByTestId("listing-channel");
    private ILocator ListingStatusBadge => _page.GetByTestId("listing-status-badge");
    private ILocator ListingProductName => _page.GetByTestId("listing-product-name");
    private ILocator ListingCreatedAt => _page.GetByTestId("listing-created-at");
    private ILocator ApproveButton => _page.GetByTestId("listing-approve-btn");
    private ILocator PauseButton => _page.GetByTestId("listing-pause-btn");
    private ILocator EndButton => _page.GetByTestId("listing-end-btn");
    private ILocator BackButton => _page.GetByTestId("listing-back-btn");
    private ILocator NotFoundAlert => _page.GetByTestId("listing-not-found");

    // ─── Navigation ────────────────────────────────────────────────────────

    public async Task NavigateAsync(string listingId)
    {
        await _page.GotoAsync($"{_baseUrl}/admin/listings/{listingId}",
            new PageGotoOptions { WaitUntil = WaitUntilState.Commit });
        await WaitForLoadedAsync();
    }

    public async Task WaitForLoadedAsync()
    {
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Wait for MudBlazor framework to hydrate (WASM pattern)
        await _page.WaitForSelectorAsync(".mud-dialog-provider",
            new() { State = WaitForSelectorState.Attached, Timeout = WasmBootstrapTimeoutMs });

        // Wait for either the page container or the not-found alert
        try
        {
            await _page.WaitForSelectorAsync(
                "[data-testid='listing-detail-page'] [data-testid='listing-sku'], [data-testid='listing-not-found']",
                new() { Timeout = ApiCallTimeoutMs });
        }
        catch (TimeoutException)
        {
            // Page may still be loading — acceptable
        }
    }

    // ─── Detail Assertions ─────────────────────────────────────────────────

    public async Task<string?> GetSkuAsync()
    {
        return await ListingSku.TextContentAsync();
    }

    public async Task<string?> GetChannelAsync()
    {
        return await ListingChannel.TextContentAsync();
    }

    public async Task<string?> GetStatusAsync()
    {
        // The status badge chip text content
        try
        {
            await ListingStatusBadge.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
            return await ListingStatusBadge.TextContentAsync();
        }
        catch (TimeoutException)
        {
            return null;
        }
    }

    public async Task<string?> GetProductNameAsync()
    {
        return await ListingProductName.TextContentAsync();
    }

    public async Task<string?> GetCreatedAtAsync()
    {
        return await ListingCreatedAt.TextContentAsync();
    }

    // ─── Action Button State ───────────────────────────────────────────────

    public async Task<bool> IsApproveButtonDisabledAsync()
    {
        try
        {
            await ApproveButton.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
            return await ApproveButton.IsDisabledAsync();
        }
        catch (TimeoutException)
        {
            return true; // Not visible means effectively disabled
        }
    }

    public async Task<bool> IsPauseButtonDisabledAsync()
    {
        try
        {
            await PauseButton.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
            return await PauseButton.IsDisabledAsync();
        }
        catch (TimeoutException)
        {
            return true;
        }
    }

    public async Task<bool> IsEndButtonDisabledAsync()
    {
        try
        {
            await EndButton.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
            return await EndButton.IsDisabledAsync();
        }
        catch (TimeoutException)
        {
            return true;
        }
    }

    // ─── Back Navigation ───────────────────────────────────────────────────

    public async Task ClickBackAsync()
    {
        await BackButton.ClickAsync();

        await _page.WaitForURLAsync(
            url => url.Contains("/admin/listings") && !url.Contains("/admin/listings/"),
            new() { Timeout = 5_000, WaitUntil = WaitUntilState.Commit });
    }

    // ─── Page State ────────────────────────────────────────────────────────

    public bool IsOnDetailPage(string listingId)
    {
        return _page.Url.Contains($"/admin/listings/{listingId}");
    }

    public async Task<bool> IsNotFoundAsync()
    {
        try
        {
            await NotFoundAlert.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }
}
