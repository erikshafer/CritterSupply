namespace Storefront.E2ETests.Pages;

/// <summary>
/// Page Object Model for the Order Confirmation page (/order-confirmation/{orderId}).
///
/// This page has two test concerns:
/// 1. Phase 1 — Static content: order ID, initial status ("Placed"), "What's Next" section
/// 2. Phase 2 — Dynamic content: SignalR real-time status updates (PaymentConfirmed, Shipped, etc.)
///
/// data-testid attributes added to OrderConfirmation.razor enable stable selection
/// without coupling to MudBlazor's internal DOM structure.
/// </summary>
public sealed class OrderConfirmationPage(IPage page)
{
    private ILocator LoadingSpinner => page.Locator("[data-testid='order-confirmation-loading']");
    private ILocator OrderNotFoundAlert => page.Locator("[data-testid='order-not-found']");
    private ILocator ConfirmationPanel => page.Locator("[data-testid='order-confirmation-panel']");
    private ILocator OrderIdDisplay => page.Locator("[data-testid='order-id']");
    private ILocator OrderStatusChip => page.Locator("[data-testid='order-status']");
    private ILocator SignalRConnectedAlert => page.Locator("[data-testid='signalr-connected']");
    private ILocator OrderUpdateNotification => page.Locator("[data-testid='order-update-notification']");
    private ILocator ContinueShoppingButton => page.GetByRole(AriaRole.Link, new() { Name = "Continue Shopping" });

    public async Task WaitForLoadAsync()
    {
        await page.WaitForSelectorAsync(
            "[data-testid='order-confirmation-panel'], [data-testid='order-not-found']",
            new PageWaitForSelectorOptions { Timeout = 15_000 });
    }

    public async Task<bool> IsOrderConfirmedAsync()
    {
        return await ConfirmationPanel.IsVisibleAsync();
    }

    public async Task<bool> IsOrderNotFoundAsync()
    {
        return await OrderNotFoundAlert.IsVisibleAsync();
    }

    public async Task<string> GetOrderIdAsync()
    {
        await OrderIdDisplay.WaitForAsync();
        return await OrderIdDisplay.InnerTextAsync();
    }

    public async Task<string> GetOrderStatusAsync()
    {
        await OrderStatusChip.WaitForAsync();
        return await OrderStatusChip.InnerTextAsync();
    }

    /// <summary>
    /// Waits for the SignalR connection indicator to appear.
    /// This confirms the Blazor component successfully connected to the SignalR hub.
    /// Timeout is intentionally generous: SignalR needs a full WebSocket handshake round-trip
    /// to the Kestrel API server, which can be slow on a loaded CI runner.
    /// </summary>
    public async Task WaitForSignalRConnectionAsync(int timeoutMs = 15_000)
    {
        await SignalRConnectedAlert.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = timeoutMs
        });
    }

    public async Task<bool> IsSignalRConnectedAsync()
    {
        return await SignalRConnectedAlert.IsVisibleAsync();
    }

    /// <summary>
    /// Waits for the order status chip to display a specific status text.
    /// Used after injecting Wolverine messages in Phase 2 SignalR tests.
    /// Uses parameterized evaluation to avoid JS injection risks.
    /// </summary>
    public async Task WaitForStatusAsync(string expectedStatus, int timeoutMs = 5_000)
    {
        // Use parameterized evaluation: passes expectedStatus as a JS argument,
        // avoiding any risk of code injection from test data values.
        await page.WaitForFunctionAsync(
            "([selector, text]) => { const el = document.querySelector(selector); return el?.innerText?.includes(text) ?? false; }",
            new object[] { "[data-testid=\"order-status\"]", expectedStatus },
            new PageWaitForFunctionOptions { Timeout = timeoutMs });
    }

    public async Task<string> GetLatestUpdateNotificationAsync()
    {
        await OrderUpdateNotification.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 5_000
        });
        return await OrderUpdateNotification.InnerTextAsync();
    }

    public async Task<bool> HasUpdateNotificationAsync()
    {
        return await OrderUpdateNotification.IsVisibleAsync();
    }
}
