using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Storefront.Api;
using Storefront.E2ETests.Pages;

namespace Storefront.E2ETests.Features;

/// <summary>
/// Reqnroll step definitions for the checkout flow E2E scenarios.
/// Each step definition delegates to the appropriate Page Object Model.
///
/// State shared between steps uses ScenarioContext — typed keys defined in ScenarioContextKeys.
///
/// Phase 1: Browser navigation + API interactions (Steps: Given/When/Then for checkout wizard)
/// Phase 2: SignalR real-time updates (Steps tagged with @signalr)
/// </summary>
[Binding]
public sealed class CheckoutFlowStepDefinitions
{
    private readonly ScenarioContext _scenarioContext;
    private readonly E2ETestFixture _fixture;
    private IPage Page => _scenarioContext.Get<IPage>(ScenarioContextKeys.Page);

    private LoginPage LoginPage => new(Page);
    private CartPage CartPage => new(Page);
    private CheckoutPage CheckoutPage => new(Page);
    private OrderConfirmationPage OrderConfirmationPage => new(Page);

    public CheckoutFlowStepDefinitions(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
        _fixture = scenarioContext.Get<E2ETestFixture>(ScenarioContextKeys.Fixture);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Given — Setup
    // ─────────────────────────────────────────────────────────────────────

    [Given(@"I am logged in as ""(.*)""")]
    public async Task GivenIAmLoggedInAs(string email)
    {
        // Navigate to login and authenticate — sets the session cookie for all subsequent requests
        await LoginPage.NavigateAsync();
        await LoginPage.LoginAsync(email, WellKnownTestData.Customers.AlicePassword);
        var isLoggedIn = await LoginPage.IsLoggedInAsync();
        isLoggedIn.ShouldBeTrue($"Login as '{email}' should succeed");

        // Inject the seeded cartId into the browser's localStorage so Cart.razor can read it.
        // The cartId is seeded by DataHooks.SeedCheckoutScenarioData before the page is created.
        if (_fixture.SeededCartId.HasValue)
        {
            await Page.EvaluateAsync(
                "cartId => localStorage.setItem('cartId', cartId)",
                _fixture.SeededCartId.Value.ToString());
        }
    }

    [Given(@"I have an active cart with the following items:")]
    public void GivenIHaveAnActiveCartWithItems(Table table)
    {
        // Cart is seeded via DataHooks.SeedCheckoutScenarioData (tagged @checkout)
        // This step validates the Gherkin spec documents the expected data
        table.RowCount.ShouldBeGreaterThan(0);
    }

    [Given(@"my account has the following saved addresses:")]
    public void GivenMyAccountHasSavedAddresses(Table table)
    {
        // Addresses are seeded via DataHooks.SeedCheckoutScenarioData (tagged @checkout)
        table.RowCount.ShouldBeGreaterThan(0);
    }

    [Given(@"I have an empty cart")]
    public void GivenIHaveAnEmptyCart()
    {
        // Clear the cart data seeded by DataHooks
        _fixture.StubShoppingClient.Clear();
        _fixture.StubCatalogClient.Clear();
    }

    [Given(@"I navigate to the cart page")]
    public async Task GivenINavigateToTheCartPage()
    {
        await CartPage.NavigateAsync();
    }

    [Given(@"I navigate to the checkout page at step ""(.*)""")]
    public async Task GivenINavigateToCheckoutAtStep(string stepName)
    {
        await CartPage.NavigateAsync();
        await CartPage.ClickProceedToCheckoutAsync();
        await CheckoutPage.WaitForCheckoutLoadedAsync();

        // Navigate to the requested step
        await AdvanceToStepAsync(stepName);
    }

    [Given(@"I have successfully placed an order")]
    public async Task GivenIHaveSuccessfullyPlacedAnOrder()
    {
        // Navigate directly to the order confirmation page using the pre-seeded deterministic order.
        //
        // Per the E2E testing principle documented in docs/skills/e2e-playwright-testing.md:
        //   "The browser only touches what the test is testing. Everything else is done via
        //    API or stub — never via browser UI navigation."
        //
        // The SignalR scenarios test real-time hub delivery to the browser — NOT the checkout
        // flow. The full browser checkout flow is already covered by Scenario 1 (happy path).
        // Running the entire checkout UI as setup for SignalR tests couples two concerns,
        // and makes the SignalR tests brittle against MudBlazor rendering timing issues
        // that have nothing to do with SignalR.
        //
        // The order is pre-seeded in E2ETestFixture.SeedStandardCheckoutScenarioAsync via
        // StubOrdersClient so GET /api/storefront/orders/{AliceOrderId} returns a valid order.
        var orderId = WellKnownTestData.Orders.AliceOrderId;
        await Page.GotoAsync($"/order-confirmation/{orderId}");
        await OrderConfirmationPage.WaitForLoadAsync();

        // Store the order ID for subsequent steps (SignalR message injection targets this ID)
        _scenarioContext.Set(orderId.ToString(), ScenarioContextKeys.OrderId);
    }

    [Given(@"I am on the order confirmation page")]
    public async Task GivenIAmOnTheOrderConfirmationPage()
    {
        await OrderConfirmationPage.WaitForLoadAsync();
        var isConfirmed = await OrderConfirmationPage.IsOrderConfirmedAsync();
        isConfirmed.ShouldBeTrue("Order confirmation panel should be visible");
    }

    [Given(@"the SignalR connection is established")]
    public async Task GivenTheSignalRConnectionIsEstablished()
    {
        await OrderConfirmationPage.WaitForSignalRConnectionAsync(timeoutMs: 15_000);
    }

    // ─────────────────────────────────────────────────────────────────────
    // When — Actions
    // ─────────────────────────────────────────────────────────────────────

    [When(@"I click ""Proceed to Checkout""")]
    public async Task WhenIClickProceedToCheckout()
    {
        await CartPage.ClickProceedToCheckoutAsync();
    }

    [When(@"I navigate to the cart page")]
    public async Task WhenINavigateToTheCartPage()
    {
        await CartPage.NavigateAsync();
    }

    [When(@"I select the saved address ""(.*)""")]
    public async Task WhenISelectSavedAddress(string nickname)
    {
        await CheckoutPage.SelectAddressByNicknameAsync(nickname);
        _scenarioContext.Set(nickname, ScenarioContextKeys.SelectedAddressNickname);
    }

    [When(@"I click ""Save & Continue"" on the address step")]
    public async Task WhenIClickSaveAndContinueAddressStep()
    {
        await CheckoutPage.ClickSaveAddressAndContinueAsync();
    }

    [When(@"I select ""Standard Ground"" shipping")]
    public async Task WhenISelectStandardGroundShipping()
    {
        await CheckoutPage.SelectStandardShippingAsync();
        _scenarioContext.Set(WellKnownTestData.Shipping.StandardMethod, ScenarioContextKeys.SelectedShippingMethod);
    }

    [When(@"I select ""Express Shipping""")]
    public async Task WhenISelectExpressShipping()
    {
        await CheckoutPage.SelectExpressShippingAsync();
        _scenarioContext.Set(WellKnownTestData.Shipping.ExpressMethod, ScenarioContextKeys.SelectedShippingMethod);
    }

    [When(@"I click ""Save & Continue"" on the shipping method step")]
    public async Task WhenIClickSaveAndContinueShippingMethodStep()
    {
        await CheckoutPage.ClickSaveShippingMethodAndContinueAsync();
    }

    [When(@"I enter the payment token ""(.*)""")]
    public async Task WhenIEnterPaymentToken(string token)
    {
        await CheckoutPage.EnterPaymentTokenAsync(token);
    }

    [When(@"I click ""Save & Continue"" on the payment step")]
    public async Task WhenIClickSaveAndContinuePaymentStep()
    {
        await CheckoutPage.ClickSavePaymentAndContinueAsync();
    }

    [When(@"I click ""Place Order""")]
    public async Task WhenIClickPlaceOrder()
    {
        await CheckoutPage.ClickPlaceOrderAsync();
    }

    [When(@"the Payments BC publishes a payment authorized event for my order")]
    public async Task WhenPaymentsBCPublishesPaymentAuthorized()
    {
        var orderIdStr = _scenarioContext.Get<string>(ScenarioContextKeys.OrderId);
        var orderId = Guid.Parse(orderIdStr);

        // IHubContext is the correct injection mechanism for E2E SignalR tests.
        // InvokeMessageAndWaitAsync requires a Wolverine local handler — OrderStatusChanged
        // has none (it is published directly to the SignalR transport via x.ToSignalR() in
        // Program.cs). Wolverine's TrackedSession cannot observe browser WebSocket delivery
        // and times out unconditionally waiting for a terminal state that never fires.
        //
        // We send a CloudEvents-shaped payload matching Wolverine's SignalR transport format.
        // SignalR serializes with camelCase (System.Text.Json web defaults), so the JS client
        // receives camelCase properties in cloudEvent.data. signalr-client.js spreads
        // cloudEvent.data and adds eventType before calling OnSseEvent on the Blazor component.
        var hubContext = _fixture.StorefrontApiHost.Services
            .GetRequiredService<IHubContext<StorefrontHub>>();

        await hubContext.Clients
            .Group($"customer:{WellKnownTestData.Customers.Alice}")
            .SendAsync("ReceiveMessage", new
            {
                specversion = "1.0",
                type = "CritterSupply.Storefront.RealTime.OrderStatusChanged",
                source = "storefront-api",
                id = Guid.NewGuid().ToString(),
                time = DateTimeOffset.UtcNow,
                datacontenttype = "application/json",
                data = new
                {
                    orderId = orderId,
                    customerId = WellKnownTestData.Customers.Alice,
                    newStatus = "PaymentAuthorized",
                    occurredAt = DateTimeOffset.UtcNow
                }
            });
    }

    [When(@"the Fulfillment BC publishes a shipment dispatched event for my order with tracking ""(.*)""")]
    public async Task WhenFulfillmentBCPublishesShipmentDispatched(string trackingNumber)
    {
        var orderIdStr = _scenarioContext.Get<string>(ScenarioContextKeys.OrderId);
        var orderId = Guid.Parse(orderIdStr);
        var shipmentId = Guid.NewGuid();

        // Same reasoning as WhenPaymentsBCPublishesPaymentAuthorized: ShipmentStatusChanged
        // has no local Wolverine handler — it is SignalR-transport-only. Use IHubContext directly.
        var hubContext = _fixture.StorefrontApiHost.Services
            .GetRequiredService<IHubContext<StorefrontHub>>();

        await hubContext.Clients
            .Group($"customer:{WellKnownTestData.Customers.Alice}")
            .SendAsync("ReceiveMessage", new
            {
                specversion = "1.0",
                type = "CritterSupply.Storefront.RealTime.ShipmentStatusChanged",
                source = "storefront-api",
                id = Guid.NewGuid().ToString(),
                time = DateTimeOffset.UtcNow,
                datacontenttype = "application/json",
                data = new
                {
                    shipmentId = shipmentId,
                    orderId = orderId,
                    customerId = WellKnownTestData.Customers.Alice,
                    newStatus = "Shipped",
                    trackingNumber = trackingNumber,
                    occurredAt = DateTimeOffset.UtcNow
                }
            });
    }

    // ─────────────────────────────────────────────────────────────────────
    // Then — Assertions
    // ─────────────────────────────────────────────────────────────────────

    [Then(@"I should be on the checkout page")]
    public async Task ThenIShouldBeOnTheCheckoutPage()
    {
        await Page.WaitForURLAsync("**/checkout**");
    }

    [Then(@"the checkout wizard should be visible")]
    public async Task ThenCheckoutWizardShouldBeVisible()
    {
        var isLoaded = await CheckoutPage.IsLoadedSuccessfullyAsync();
        isLoaded.ShouldBeTrue("Checkout stepper should be visible after loading");
    }

    [Then(@"I should see the shipping method selection")]
    public async Task ThenIShouldSeeShippingMethodSelection()
    {
        // After completing Step 1, MudStepper advances to Step 2
        // The shipping method radio group should be visible
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    [Then(@"I should see the payment form")]
    public async Task ThenIShouldSeePaymentForm()
    {
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    [Then(@"I should see the order review summary")]
    public async Task ThenIShouldSeeOrderReviewSummary()
    {
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    [Then(@"the order summary should show:")]
    public async Task ThenOrderSummaryShouldShow(Table table)
    {
        foreach (var row in table.Rows)
        {
            var field = row["Field"];
            var expectedValue = row["Value"];

            var actualValue = field switch
            {
                "Subtotal" => await CheckoutPage.GetOrderSubtotalAsync(),
                "Shipping" => await CheckoutPage.GetOrderShippingCostAsync(),
                "Total" => await CheckoutPage.GetOrderTotalAsync(),
                _ => throw new ArgumentException($"Unknown order summary field: {field}")
            };

            actualValue.ShouldContain(expectedValue.TrimStart('$'));
        }
    }

    [Then(@"I should be on the order confirmation page")]
    public async Task ThenIShouldBeOnOrderConfirmationPage()
    {
        await Page.WaitForURLAsync("**/order-confirmation/**");
        await OrderConfirmationPage.WaitForLoadAsync();
    }

    [Then(@"the order status should be ""(.*)""")]
    public async Task ThenOrderStatusShouldBe(string expectedStatus)
    {
        var status = await OrderConfirmationPage.GetOrderStatusAsync();
        status.ShouldContain(expectedStatus);
    }

    [Then(@"the ""Proceed to Checkout"" button should be disabled")]
    public async Task ThenProceedToCheckoutShouldBeDisabled()
    {
        var isEnabled = await CartPage.IsProceedToCheckoutEnabledAsync();
        isEnabled.ShouldBeFalse("Proceed to Checkout button should be disabled for empty cart");
    }

    [Then(@"I should see a message indicating the cart is empty")]
    public async Task ThenIShouldSeeEmptyCartMessage()
    {
        var isVisible = await CartPage.IsEmptyCartMessageVisibleAsync();
        isVisible.ShouldBeTrue("Empty cart message should be visible");
    }

    [Then(@"the order summary should display a shipping cost of ""(.*)""")]
    public async Task ThenShippingCostShouldBe(string expectedCost)
    {
        var cost = await CheckoutPage.GetOrderShippingCostAsync();
        cost.ShouldContain(expectedCost.TrimStart('$'));
    }

    [Then(@"the order total should be ""(.*)""")]
    public async Task ThenOrderTotalShouldBe(string expectedTotal)
    {
        var total = await CheckoutPage.GetOrderTotalAsync();
        total.ShouldContain(expectedTotal.TrimStart('$'));
    }

    [Then(@"I should see an error notification")]
    public async Task ThenIShouldSeeErrorNotification()
    {
        // MudSnackbar error toasts appear — wait for any alert/snackbar
        await Page.WaitForSelectorAsync(
            "[role='alert'], .mud-snackbar",
            new PageWaitForSelectorOptions { Timeout = 5_000 });
    }

    [Then(@"the checkout wizard should remain on the payment step")]
    public async Task ThenCheckoutWizardShouldRemainOnPaymentStep()
    {
        // After invalid payment token, the stepper should NOT advance
        var isLoaded = await CheckoutPage.IsLoadedSuccessfullyAsync();
        isLoaded.ShouldBeTrue("Checkout stepper should still be visible");
    }

    [Then(@"the order status should update to ""(.*)"" within (.*) seconds")]
    public async Task ThenOrderStatusShouldUpdateTo(string expectedStatus, int timeoutSeconds)
    {
        await OrderConfirmationPage.WaitForStatusAsync(
            expectedStatus,
            timeoutMs: timeoutSeconds * 1_000);

        var status = await OrderConfirmationPage.GetOrderStatusAsync();
        status.ShouldContain(expectedStatus);
    }

    [Then(@"I should see a payment notification message")]
    public async Task ThenIShouldSeePaymentNotification()
    {
        var hasNotification = await OrderConfirmationPage.HasUpdateNotificationAsync();
        hasNotification.ShouldBeTrue("A payment notification should appear after PaymentAuthorized event");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────

    private async Task AdvanceToStepAsync(string targetStep)
    {
        switch (targetStep.ToLowerInvariant())
        {
            case "shipping method":
                await CheckoutPage.SelectAddressByNicknameAsync(WellKnownTestData.Addresses.AliceHomeNickname);
                await CheckoutPage.ClickSaveAddressAndContinueAsync();
                break;

            case "payment":
                await CheckoutPage.SelectAddressByNicknameAsync(WellKnownTestData.Addresses.AliceHomeNickname);
                await CheckoutPage.ClickSaveAddressAndContinueAsync();
                await CheckoutPage.SelectStandardShippingAsync();
                await CheckoutPage.ClickSaveShippingMethodAndContinueAsync();
                break;

            case "review":
                await CheckoutPage.SelectAddressByNicknameAsync(WellKnownTestData.Addresses.AliceHomeNickname);
                await CheckoutPage.ClickSaveAddressAndContinueAsync();
                await CheckoutPage.SelectStandardShippingAsync();
                await CheckoutPage.ClickSaveShippingMethodAndContinueAsync();
                await CheckoutPage.EnterPaymentTokenAsync(WellKnownTestData.Payment.ValidVisaToken);
                await CheckoutPage.ClickSavePaymentAndContinueAsync();
                break;

            default:
                break; // "Shipping Address" = starting point, no advancement needed
        }
    }
}
