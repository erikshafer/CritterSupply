namespace Storefront.E2ETests.Pages;

/// <summary>
/// Page Object Model for the Cart page (/cart).
///
/// Encapsulates all Playwright interactions with the cart page,
/// decoupled from MudBlazor's internal CSS structure via data-testid attributes.
/// </summary>
public sealed class CartPage(IPage page)
{
    private ILocator ProceedToCheckoutButton => page.GetByRole(AriaRole.Button, new() { Name = "Proceed to Checkout" });
    private ILocator EmptyCartMessage => page.GetByText("Your cart is empty.");
    private ILocator CartItems => page.Locator("[data-testid^='cart-item-']");

    public async Task NavigateAsync()
    {
        await page.GotoAsync("/cart");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    public async Task ClickProceedToCheckoutAsync()
    {
        await ProceedToCheckoutButton.ClickAsync();
        // Wait for redirect to /checkout and checkout data to load
        await page.WaitForURLAsync("**/checkout**");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    public async Task<bool> IsProceedToCheckoutEnabledAsync()
    {
        // When cart is empty, the button is not rendered — treat as disabled rather than timing out
        if (!await ProceedToCheckoutButton.IsVisibleAsync())
            return false;
        return !await ProceedToCheckoutButton.IsDisabledAsync();
    }

    public async Task<bool> IsEmptyCartMessageVisibleAsync()
    {
        return await EmptyCartMessage.IsVisibleAsync();
    }

    public async Task<int> GetCartItemCountAsync()
    {
        return await CartItems.CountAsync();
    }
}
