using Orders.Checkout;
using ShoppingContracts = Messages.Contracts.Shopping;

namespace Orders.Api.IntegrationTests.Checkout;

[Collection(IntegrationTestCollection.Name)]
public class CheckoutWorkflowTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public CheckoutWorkflowTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.CleanAllDocumentsAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task CheckoutInitiated_FromShopping_CreatesCheckoutStream()
    {
        // Arrange - Simulate Shopping BC publishing CheckoutInitiated
        var checkoutId = Guid.CreateVersion7();
        var cartId = Guid.CreateVersion7();
        var customerId = Guid.CreateVersion7();
        var items = new List<ShoppingContracts.CheckoutLineItem>
        {
            new("SKU-001", 2, 19.99m)
        };

        var message = new ShoppingContracts.CheckoutInitiated(
            checkoutId,
            cartId,
            customerId,
            items,
            DateTimeOffset.UtcNow);

        // Act - Handle integration message
        await _fixture.ExecuteAndWaitAsync(message);

        // Assert - Verify checkout stream created
        using var session = _fixture.GetDocumentSession();
        var checkout = await session.LoadAsync<Orders.Checkout.Checkout>(checkoutId);

        checkout.ShouldNotBeNull();
        checkout.CartId.ShouldBe(cartId);
        checkout.CustomerId.ShouldBe(customerId);
        checkout.Items.ShouldNotBeEmpty();
        checkout.Items.Count.ShouldBe(1);
        checkout.Items[0].Sku.ShouldBe("SKU-001");
        checkout.Items[0].Quantity.ShouldBe(2);
        checkout.IsCompleted.ShouldBeFalse();
    }

    [Fact]
    public async Task ProvideShippingAddress_UpdatesCheckout()
    {
        // Arrange - Create checkout
        var checkoutId = await CreateCheckoutWithItems();

        // Act - Provide shipping address via HTTP (Direct Implementation pattern)
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new ProvideShippingAddressRequest(
                "123 Main St", null, "Seattle", "WA", "98101", "USA"))
                .ToUrl($"/api/checkouts/{checkoutId}/shipping-address");
            x.StatusCodeShouldBeOk();
        });

        // Assert - Verify address stored
        using var session = _fixture.GetDocumentSession();
        var checkout = await session.LoadAsync<Orders.Checkout.Checkout>(checkoutId);

        checkout.ShouldNotBeNull();
        checkout.ShippingAddress.ShouldNotBeNull();
        checkout.ShippingAddress.AddressLine1.ShouldBe("123 Main St");
        checkout.ShippingAddress.City.ShouldBe("Seattle");
        checkout.ShippingAddress.StateOrProvince.ShouldBe("WA");
        checkout.ShippingAddress.PostalCode.ShouldBe("98101");
        checkout.ShippingAddress.Country.ShouldBe("USA");
    }

    [Fact]
    public async Task SelectShippingMethod_UpdatesCheckout()
    {
        // Arrange - Create checkout
        var checkoutId = await CreateCheckoutWithItems();

        // Act - Select shipping method via HTTP
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new SelectShippingMethodRequest("Standard Ground", 5.99m))
                .ToUrl($"/api/checkouts/{checkoutId}/shipping-method");
            x.StatusCodeShouldBeOk();
        });

        // Assert - Verify shipping method stored
        using var session = _fixture.GetDocumentSession();
        var checkout = await session.LoadAsync<Orders.Checkout.Checkout>(checkoutId);

        checkout.ShouldNotBeNull();
        checkout.ShippingMethod.ShouldBe("Standard Ground");
        checkout.ShippingCost.ShouldBe(5.99m);
        checkout.Total.ShouldBe(checkout.Subtotal + 5.99m);
    }

    [Fact]
    public async Task ProvidePaymentMethod_UpdatesCheckout()
    {
        // Arrange - Create checkout
        var checkoutId = await CreateCheckoutWithItems();

        // Act - Provide payment method via HTTP
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new ProvidePaymentMethodRequest("tok_visa_4242"))
                .ToUrl($"/api/checkouts/{checkoutId}/payment-method");
            x.StatusCodeShouldBeOk();
        });

        // Assert - Verify payment token stored
        using var session = _fixture.GetDocumentSession();
        var checkout = await session.LoadAsync<Orders.Checkout.Checkout>(checkoutId);

        checkout.ShouldNotBeNull();
        checkout.PaymentMethodToken.ShouldBe("tok_visa_4242");
    }

    [Fact]
    public async Task CompleteCheckout_WithAllRequiredInfo_CompletesCheckout()
    {
        // Arrange - Create checkout with all required info via HTTP
        var checkoutId = await CreateCheckoutWithItems();
        await ProvideAllCheckoutStepsViaHttp(checkoutId);

        // Act - Complete checkout via HTTP
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new { }).ToUrl($"/api/checkouts/{checkoutId}/complete");
            x.StatusCodeShouldBeOk();
        });

        // Assert - Verify checkout completed
        using var session = _fixture.GetDocumentSession();
        var checkout = await session.LoadAsync<Orders.Checkout.Checkout>(checkoutId);

        checkout.ShouldNotBeNull();
        checkout.IsCompleted.ShouldBeTrue();
    }

    [Fact]
    public async Task CompleteCheckout_WithAllStepsCompleted_VerifiesAllData()
    {
        // Arrange - Create checkout and complete all steps via HTTP
        var checkoutId = await CreateCheckoutWithItems();
        await ProvideAllCheckoutStepsViaHttp(checkoutId);

        // Act - Complete checkout via HTTP
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new { }).ToUrl($"/api/checkouts/{checkoutId}/complete");
            x.StatusCodeShouldBeOk();
        });

        // Assert - Verify checkout is marked complete with all required data
        using var session = _fixture.GetDocumentSession();
        var checkout = await session.LoadAsync<Orders.Checkout.Checkout>(checkoutId);

        checkout.ShouldNotBeNull();
        checkout.IsCompleted.ShouldBeTrue();
        checkout.ShippingAddress.ShouldNotBeNull();
        checkout.ShippingAddress.AddressLine1.ShouldBe("123 Main St");
        checkout.ShippingMethod.ShouldBe("Standard");
        checkout.ShippingCost.ShouldBe(5.99m);
        checkout.PaymentMethodToken.ShouldBe("tok_visa_4242");
        checkout.Items.ShouldNotBeEmpty();
    }

    private async Task<Guid> CreateCheckoutWithItems()
    {
        var checkoutId = Guid.CreateVersion7();
        var cartId = Guid.CreateVersion7();
        var customerId = Guid.CreateVersion7();
        var items = new List<ShoppingContracts.CheckoutLineItem>
        {
            new("SKU-001", 2, 19.99m)
        };

        var message = new ShoppingContracts.CheckoutInitiated(
            checkoutId,
            cartId,
            customerId,
            items,
            DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(message);

        return checkoutId;
    }

    private async Task ProvideAllCheckoutStepsViaHttp(Guid checkoutId)
    {
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new ProvideShippingAddressRequest(
                "123 Main St", null, "Seattle", "WA", "98101", "USA"))
                .ToUrl($"/api/checkouts/{checkoutId}/shipping-address");
            x.StatusCodeShouldBeOk();
        });

        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new SelectShippingMethodRequest("Standard", 5.99m))
                .ToUrl($"/api/checkouts/{checkoutId}/shipping-method");
            x.StatusCodeShouldBeOk();
        });

        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new ProvidePaymentMethodRequest("tok_visa_4242"))
                .ToUrl($"/api/checkouts/{checkoutId}/payment-method");
            x.StatusCodeShouldBeOk();
        });
    }
}
