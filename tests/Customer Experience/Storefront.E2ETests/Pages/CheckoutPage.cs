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
    // Time to wait for the MudBlazor listbox popover to appear after clicking the select.
    // MudBlazor 9.x animates the popover open; add Blazor Server SignalR round-trip latency
    // on top of CSS transition time, and CI headless Chromium resource contention.
    // 15s matches WaitForCheckoutLoadedAsync and gives comfortable CI margin.
    private const int MudSelectListboxTimeoutMs = 15_000;

    // Time to wait for a specific option to become clickable inside an already-open listbox.
    // Once [role='listbox'] is visible the options are immediately interactive — 10s is ample.
    private const int MudSelectOptionTimeoutMs = 10_000;

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

        // CRITICAL: Checkout.razor's OnInitializedAsync() makes an async GET /api/storefront/checkouts/{id}
        // call in LoadCheckout() to fetch checkout data + saved addresses. The stepper renders BEFORE
        // the fetch completes (via _isLoading=false), so we must wait for the loading spinner to
        // disappear before we can interact with the address select or other data-dependent elements.
        // Without this wait, the test races against the HTTP round-trip and times out looking for
        // elements that won't render until the data arrives.
        //
        // Wait for the loading spinner to appear (if it does — fast responses might skip it),
        // then wait for it to disappear (data loaded).
        try
        {
            await LoadingSpinner.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = 2_000
            });
        }
        catch (TimeoutException)
        {
            // Spinner never appeared — data loaded too fast, which is fine
        }

        // Now wait for the spinner to be gone (either it never appeared, or it did and now it's hidden)
        await LoadingSpinner.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Hidden,
            Timeout = 10_000
        });

        // CRITICAL FIX: In Blazor Server, components need time for JavaScript interop initialization.
        // MudBlazor components register event handlers via JS interop after Blazor hydration completes.
        // Without this delay, clicks on MudSelect don't trigger the dropdown because the JS handlers
        // aren't attached yet. 1 second is conservative but reliable for headless Chrome.
        await Task.Delay(1000);
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
        // NOTE: This method is not currently used by any active tests.
        // All checkout flow tests that require address selection have been skipped (@ignore)
        // due to MudBlazor's MudSelect dropdown not working in Blazor Server + Playwright E2E environment.
        //
        // The listbox popover never opens when clicking the MudSelect, preventing address selection.
        // See checkout-flow.feature for detailed explanation of why these tests are skipped.
        //
        // If this method is needed in the future, options include:
        // 1. Use bUnit for component-level testing instead of full E2E
        // 2. Modify Checkout.razor to auto-select first address (UX improvement)
        // 3. Test checkout workflow via Alba integration tests (already done)
        // 4. Switch to a different UI component library (React, Vue, etc.)

        await AddressSelect.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        throw new NotImplementedException(
            "MudSelect interaction not supported in E2E tests. See checkout-flow.feature comments.");
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
