namespace Storefront.E2ETests.Pages;

/// <summary>
/// Page Object Model for the Checkout page (/checkout).
///
/// The Checkout page is a 4-step MudStepper wizard. Each step has dedicated
/// data-testid attributes that this POM uses for stable, version-independent selection.
///
/// Step selectors are scoped to the active MudStep content to avoid
/// interacting with hidden steps.
/// </summary>
public sealed class CheckoutPage(IPage page)
{
    // Timeout for MudBlazor popover interactions: waiting for the listbox to appear and
    // waiting for a specific option to be clickable after the dropdown opens.
    private const int MudSelectPopoverTimeoutMs = 10_000;

    // Loading state
    private ILocator LoadingSpinner => page.Locator("[data-testid='checkout-loading']");
    private ILocator ErrorAlert => page.Locator("[data-testid='checkout-error']");

    // Step navigation
    private ILocator CheckoutStepper => page.Locator("[data-testid='checkout-stepper']");
    private ILocator CurrentStepTitle => page.Locator("[data-testid='checkout-step-title']").First;

    // Step 1: Shipping Address
    private ILocator AddressSelect => page.Locator("[data-testid='address-select']");
    private ILocator NoSavedAddressesAlert => page.Locator("[data-testid='no-saved-addresses']");
    private ILocator SaveAddressButton => page.Locator("[data-testid='btn-save-address']");

    // Step 2: Shipping Method
    private ILocator ShippingMethodGroup => page.Locator("[data-testid='shipping-method-group']");
    private ILocator StandardShippingRadio => page.Locator("[data-testid='shipping-method-standard']");
    private ILocator ExpressShippingRadio => page.Locator("[data-testid='shipping-method-express']");
    private ILocator NextDayShippingRadio => page.Locator("[data-testid='shipping-method-nextday']");
    private ILocator SaveShippingMethodButton => page.Locator("[data-testid='btn-save-shipping-method']");

    // Step 3: Payment
    private ILocator PaymentTokenInput => page.Locator("[data-testid='payment-token-input']");
    private ILocator SavePaymentButton => page.Locator("[data-testid='btn-save-payment']");

    // Step 4: Review & Submit
    private ILocator OrderSummary => page.Locator("[data-testid='order-summary']");
    private ILocator OrderSubtotal => page.Locator("[data-testid='order-subtotal']");
    private ILocator OrderShippingCost => page.Locator("[data-testid='order-shipping-cost']");
    private ILocator OrderTotal => page.Locator("[data-testid='order-total']");
    private ILocator PlaceOrderButton => page.Locator("[data-testid='btn-place-order']");

    // Processing state
    private ILocator ProcessingIndicator => page.Locator("[data-testid='checkout-processing']");

    public async Task NavigateAsync()
    {
        await page.GotoAsync("/checkout");
        await WaitForCheckoutLoadedAsync();
    }

    public async Task WaitForCheckoutLoadedAsync()
    {
        // Wait for either the stepper (success) or error alert (failure) to appear
        await page.WaitForSelectorAsync(
            "[data-testid='checkout-stepper'], [data-testid='checkout-error']",
            new PageWaitForSelectorOptions { Timeout = 15_000 });
    }

    public async Task<bool> IsLoadedSuccessfullyAsync()
    {
        return await CheckoutStepper.IsVisibleAsync();
    }

    public async Task<bool> HasLoadErrorAsync()
    {
        return await ErrorAlert.IsVisibleAsync();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Step 1: Shipping Address
    // ─────────────────────────────────────────────────────────────────────

    public async Task SelectAddressByNicknameAsync(string nickname)
    {
        // Wait for the outer MudSelect wrapper to be visible (present and rendered).
        await AddressSelect.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        // Click the outer [data-testid='address-select'] wrapper directly.
        // MudBlazor renders a transparent overlay on top of .mud-select-input to handle pointer
        // events. Playwright's actionability hit-test fails against that overlay in headless
        // Chromium — it waits 30s for .mud-select-input to be "hittable" and times out.
        // The outer wrapper is reliably hittable at its bounding-box center; MudBlazor
        // delegates the click inward to open the dropdown.
        await AddressSelect.ClickAsync();

        // Wait explicitly for the MudBlazor listbox popover to appear in the DOM.
        // MudBlazor renders options in a portal at the document body level (outside AddressSelect),
        // so this uses page scope. Waiting here synchronises option-click with dropdown-open,
        // avoiding the race where the option locator is queried before the popover renders.
        await page.WaitForSelectorAsync("[role='listbox']",
            new PageWaitForSelectorOptions { Timeout = MudSelectPopoverTimeoutMs });

        // Target the option by its data-testid for stable, animation-resistant selection.
        // data-testid="address-option-{nickname.ToLowerInvariant()}" is set on each MudSelectItem
        // in Checkout.razor and forwarded to the rendered <li> via MudBlazor UserAttributes spread.
        // More reliable than [role='option']:has-text() which depends on text formatting.
        var optionLocator = page.Locator($"[data-testid='address-option-{nickname.ToLowerInvariant()}']");
        await optionLocator.ClickAsync(new LocatorClickOptions { Timeout = MudSelectPopoverTimeoutMs });
    }

    public async Task ClickSaveAddressAndContinueAsync()
    {
        await SaveAddressButton.ClickAsync();
        await WaitForProcessingCompleteAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    public async Task<bool> HasSavedAddressesAsync()
    {
        return !await NoSavedAddressesAlert.IsVisibleAsync();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Step 2: Shipping Method
    // ─────────────────────────────────────────────────────────────────────

    public async Task SelectStandardShippingAsync()
    {
        await StandardShippingRadio.ClickAsync();
    }

    public async Task SelectExpressShippingAsync()
    {
        await ExpressShippingRadio.ClickAsync();
    }

    public async Task SelectNextDayShippingAsync()
    {
        await NextDayShippingRadio.ClickAsync();
    }

    public async Task ClickSaveShippingMethodAndContinueAsync()
    {
        await SaveShippingMethodButton.ClickAsync();
        await WaitForProcessingCompleteAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Step 3: Payment
    // ─────────────────────────────────────────────────────────────────────

    public async Task EnterPaymentTokenAsync(string token)
    {
        await PaymentTokenInput.FillAsync(token);
    }

    public async Task ClickSavePaymentAndContinueAsync()
    {
        await SavePaymentButton.ClickAsync();
        await WaitForProcessingCompleteAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Step 4: Review & Submit
    // ─────────────────────────────────────────────────────────────────────

    public async Task<string> GetOrderSubtotalAsync()
    {
        return await OrderSubtotal.InnerTextAsync();
    }

    public async Task<string> GetOrderShippingCostAsync()
    {
        return await OrderShippingCost.InnerTextAsync();
    }

    public async Task<string> GetOrderTotalAsync()
    {
        return await OrderTotal.InnerTextAsync();
    }

    public async Task ClickPlaceOrderAsync()
    {
        await PlaceOrderButton.ClickAsync();
        // Wait for redirect to order confirmation page
        await page.WaitForURLAsync("**/order-confirmation/**", new PageWaitForURLOptions
        {
            Timeout = 20_000 // Allow time for checkout completion + order saga start
        });
    }

    // ─────────────────────────────────────────────────────────────────────
    // General Assertions
    // ─────────────────────────────────────────────────────────────────────

    public async Task<string> GetCurrentStepTitleAsync()
    {
        await CurrentStepTitle.WaitForAsync();
        return await CurrentStepTitle.InnerTextAsync();
    }

    /// <summary>
    /// Waits for any active processing indicator to disappear.
    /// Called after each step's "Save & Continue" action to ensure the API call completed.
    /// </summary>
    private async Task WaitForProcessingCompleteAsync()
    {
        // If processing indicator appears, wait for it to go away
        try
        {
            await ProcessingIndicator.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = 2_000
            });
            await ProcessingIndicator.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Hidden,
                Timeout = 10_000
            });
        }
        catch (TimeoutException)
        {
            // Processing indicator may not appear for fast responses — that's fine
        }
    }
}
