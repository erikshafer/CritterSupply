using Marten;
using Shopping.Cart;
using Shopping.Checkout;

namespace Shopping.Api.IntegrationTests.Checkout;

[Collection(nameof(IntegrationTestCollection))]
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
    public async Task InitiateCheckout_FromCart_CreatesCheckoutStream()
    {
        // Arrange - Create cart with items
        var customerId = Guid.CreateVersion7();
        var initCart = new InitializeCart(customerId, null);
        await _fixture.ExecuteAndWaitAsync(initCart);

        using var session = _fixture.GetDocumentSession();
        var cart = await session.Query<Shopping.Cart.Cart>()
            .Where(c => c.CustomerId == customerId)
            .SingleAsync();

        var addItem = new AddItemToCart(cart.Id, "SKU-001", 2, 19.99m);
        await _fixture.ExecuteAndWaitAsync(addItem);

        // Act - Initiate checkout
        var initiateCheckout = new InitiateCheckout(cart.Id);
        await _fixture.ExecuteAndWaitAsync(initiateCheckout);

        // Assert - Verify checkout stream created
        var checkout = await session.Query<Shopping.Checkout.Checkout>()
            .Where(c => c.CartId == cart.Id)
            .SingleAsync();

        checkout.CartId.ShouldBe(cart.Id);
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

        // Act - Provide shipping address
        var command = new ProvideShippingAddress(
            checkoutId,
            "123 Main St",
            null,
            "Seattle",
            "WA",
            "98101",
            "USA");
        await _fixture.ExecuteAndWaitAsync(command);

        // Assert - Verify address stored
        using var session = _fixture.GetDocumentSession();
        var checkout = await session.LoadAsync<Shopping.Checkout.Checkout>(checkoutId);

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

        // Act - Select shipping method
        var command = new SelectShippingMethod(checkoutId, "Standard Ground", 5.99m);
        await _fixture.ExecuteAndWaitAsync(command);

        // Assert - Verify shipping method stored
        using var session = _fixture.GetDocumentSession();
        var checkout = await session.LoadAsync<Shopping.Checkout.Checkout>(checkoutId);

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

        // Act - Provide payment method
        var command = new ProvidePaymentMethod(checkoutId, "tok_visa_4242");
        await _fixture.ExecuteAndWaitAsync(command);

        // Assert - Verify payment token stored
        using var session = _fixture.GetDocumentSession();
        var checkout = await session.LoadAsync<Shopping.Checkout.Checkout>(checkoutId);

        checkout.ShouldNotBeNull();
        checkout.PaymentMethodToken.ShouldBe("tok_visa_4242");
    }

    [Fact]
    public async Task CompleteCheckout_WithAllRequiredInfo_CompletesCheckout()
    {
        // Arrange - Create checkout with all required info
        var checkoutId = await CreateCheckoutWithItems();

        await _fixture.ExecuteAndWaitAsync(new ProvideShippingAddress(
            checkoutId, "123 Main St", null, "Seattle", "WA", "98101", "USA"));
        await _fixture.ExecuteAndWaitAsync(new SelectShippingMethod(checkoutId, "Standard", 5.99m));
        await _fixture.ExecuteAndWaitAsync(new ProvidePaymentMethod(checkoutId, "tok_visa_4242"));

        // Act - Complete checkout
        var command = new CompleteCheckout(checkoutId);
        await _fixture.ExecuteAndWaitAsync(command);

        // Assert - Verify checkout completed
        using var session = _fixture.GetDocumentSession();
        var checkout = await session.LoadAsync<Shopping.Checkout.Checkout>(checkoutId);

        checkout.ShouldNotBeNull();
        checkout.IsCompleted.ShouldBeTrue();
    }

    [Fact]
    public async Task CompleteCheckout_WithAllStepsCompleted_VerifiesAllData()
    {
        // Arrange - Create checkout and complete all steps
        var checkoutId = await CreateCheckoutWithItems();

        await _fixture.ExecuteAndWaitAsync(new ProvideShippingAddress(
            checkoutId, "123 Main St", null, "Seattle", "WA", "98101", "USA"));
        await _fixture.ExecuteAndWaitAsync(new SelectShippingMethod(checkoutId, "Standard", 5.99m));
        await _fixture.ExecuteAndWaitAsync(new ProvidePaymentMethod(checkoutId, "tok_visa_4242"));

        // Act - Complete checkout
        var command = new CompleteCheckout(checkoutId);
        await _fixture.ExecuteAndWaitAsync(command);

        // Assert - Verify checkout is marked complete with all required data
        // Note: Integration message (CheckoutCompleted) is published to Orders BC but we don't
        // verify outgoing messages in integration tests since DisableAllExternalWolverineTransports()
        // prevents actual message routing. Message handling is verified in Orders BC integration tests.
        using var session = _fixture.GetDocumentSession();
        var checkout = await session.LoadAsync<Shopping.Checkout.Checkout>(checkoutId);

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
        var customerId = Guid.CreateVersion7();
        var initCart = new InitializeCart(customerId, null);
        await _fixture.ExecuteAndWaitAsync(initCart);

        using var session = _fixture.GetDocumentSession();
        var cart = await session.Query<Shopping.Cart.Cart>()
            .Where(c => c.CustomerId == customerId)
            .SingleAsync();

        var addItem = new AddItemToCart(cart.Id, "SKU-001", 2, 19.99m);
        await _fixture.ExecuteAndWaitAsync(addItem);

        var initiateCheckout = new InitiateCheckout(cart.Id);
        await _fixture.ExecuteAndWaitAsync(initiateCheckout);

        var checkout = await session.Query<Shopping.Checkout.Checkout>()
            .Where(c => c.CartId == cart.Id)
            .SingleAsync();

        return checkout.Id;
    }
}
