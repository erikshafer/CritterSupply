using System.Net;

namespace Storefront.Api.IntegrationTests;

/// <summary>
/// Tests for cart mutation commands via BFF → Shopping BC delegation
/// Verifies command delegation, error handling, and proper HTTP responses
/// </summary>
[Collection("Sequential")]
public class CartCommandTests(TestFixture fixture) : IClassFixture<TestFixture>, IAsyncLifetime
{
    public Task InitializeAsync()
    {
        fixture.ClearAllStubs();
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task InitializeCart_CreatesCartSuccessfully()
    {
        // GIVEN: A customer ID
        var customerId = Guid.NewGuid();

        // WHEN: The BFF initializes a cart
        var result = await fixture.Host.Scenario(scenario =>
        {
            scenario.Post.Json(new { customerId }).ToUrl("/api/storefront/carts/initialize");
            scenario.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        // THEN: A cart ID should be returned
        var cartId = await result.ReadAsJsonAsync<Guid>();
        cartId.ShouldNotBe(Guid.Empty);

        // AND: The cart should exist in Shopping BC stub
        var cart = await fixture.StubShoppingClient.GetCartAsync(cartId);
        cart.ShouldNotBeNull();
        cart.CustomerId.ShouldBe(customerId);
        cart.Items.ShouldBeEmpty(); // New cart has no items
    }

    [Fact]
    public async Task AddItemToCart_AddsItemSuccessfully()
    {
        // GIVEN: An existing cart
        var customerId = Guid.NewGuid();
        var cartId = await fixture.StubShoppingClient.InitializeCartAsync(customerId);

        // WHEN: The BFF adds an item to the cart
        var result = await fixture.Host.Scenario(scenario =>
        {
            scenario.Post.Json(new { sku = "DOG-BOWL-001", quantity = 2, unitPrice = 19.99m })
                .ToUrl($"/api/storefront/carts/{cartId}/items");
            scenario.StatusCodeShouldBe(HttpStatusCode.NoContent); // 204
        });

        // THEN: The item should be in the cart
        var cart = await fixture.StubShoppingClient.GetCartAsync(cartId);
        cart.ShouldNotBeNull();
        cart.Items.Count.ShouldBe(1);

        var item = cart.Items.Single();
        item.Sku.ShouldBe("DOG-BOWL-001");
        item.Quantity.ShouldBe(2);
        item.UnitPrice.ShouldBe(19.99m);
    }

    [Fact]
    public async Task AddItemToCart_WhenCartNotFound_Returns404()
    {
        // GIVEN: A cart ID that does not exist
        var nonExistentCartId = Guid.NewGuid();

        // WHEN: The BFF attempts to add an item
        var result = await fixture.Host.Scenario(scenario =>
        {
            scenario.Post.Json(new { sku = "DOG-BOWL-001", quantity = 1, unitPrice = 19.99m })
                .ToUrl($"/api/storefront/carts/{nonExistentCartId}/items");
            scenario.StatusCodeShouldBe(HttpStatusCode.NotFound); // 404
        });

        // THEN: A 404 response should be returned
        result.Context.Response.StatusCode.ShouldBe((int)HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RemoveItemFromCart_RemovesItemSuccessfully()
    {
        // GIVEN: A cart with an item
        var customerId = Guid.NewGuid();
        var cartId = await fixture.CreateCartWithItemsAsync(customerId, ("DOG-BOWL-001", 2, 19.99m));

        // WHEN: The BFF removes the item
        var result = await fixture.Host.Scenario(scenario =>
        {
            scenario.Delete.Url($"/api/storefront/carts/{cartId}/items/DOG-BOWL-001");
            scenario.StatusCodeShouldBe(HttpStatusCode.NoContent); // 204
        });

        // THEN: The cart should be empty
        var cart = await fixture.StubShoppingClient.GetCartAsync(cartId);
        cart.ShouldNotBeNull();
        cart.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task RemoveItemFromCart_WhenCartNotFound_Returns404()
    {
        // GIVEN: A cart ID that does not exist
        var nonExistentCartId = Guid.NewGuid();

        // WHEN: The BFF attempts to remove an item
        await fixture.Host.Scenario(scenario =>
        {
            scenario.Delete.Url($"/api/storefront/carts/{nonExistentCartId}/items/DOG-BOWL-001");
            scenario.StatusCodeShouldBe(HttpStatusCode.NotFound); // 404 - Cart not found
        });
    }

    [Fact]
    public async Task ChangeItemQuantity_UpdatesQuantitySuccessfully()
    {
        // GIVEN: A cart with an item (quantity 2)
        var customerId = Guid.NewGuid();
        var cartId = await fixture.CreateCartWithItemsAsync(customerId, ("DOG-BOWL-001", 2, 19.99m));

        // WHEN: The BFF changes the quantity to 5
        var result = await fixture.Host.Scenario(scenario =>
        {
            scenario.Put.Json(new { newQuantity = 5 })
                .ToUrl($"/api/storefront/carts/{cartId}/items/DOG-BOWL-001/quantity");
            scenario.StatusCodeShouldBe(HttpStatusCode.NoContent); // 204
        });

        // THEN: The quantity should be updated
        var cart = await fixture.StubShoppingClient.GetCartAsync(cartId);
        cart.ShouldNotBeNull();
        cart.Items.Count.ShouldBe(1);

        var item = cart.Items.Single();
        item.Quantity.ShouldBe(5);
    }

    [Fact]
    public async Task ChangeItemQuantity_WhenCartNotFound_Returns404()
    {
        // GIVEN: A cart ID that does not exist
        var nonExistentCartId = Guid.NewGuid();

        // WHEN: The BFF attempts to change item quantity
        await fixture.Host.Scenario(scenario =>
        {
            scenario.Put.Json(new { newQuantity = 5 })
                .ToUrl($"/api/storefront/carts/{nonExistentCartId}/items/DOG-BOWL-001/quantity");
            scenario.StatusCodeShouldBe(HttpStatusCode.NotFound); // 404 - Cart not found
        });
    }

    [Fact]
    public async Task ChangeItemQuantity_ValidatesQuantityGreaterThanZero()
    {
        // GIVEN: A cart with an item
        var customerId = Guid.NewGuid();
        var cartId = await fixture.CreateCartWithItemsAsync(customerId, ("DOG-BOWL-001", 2, 19.99m));

        // WHEN: The BFF attempts to change quantity to 0
        var result = await fixture.Host.Scenario(scenario =>
        {
            scenario.Put.Json(new { newQuantity = 0 })
                .ToUrl($"/api/storefront/carts/{cartId}/items/DOG-BOWL-001/quantity");
            // FluentValidation should reject this
            scenario.StatusCodeShouldBe(HttpStatusCode.BadRequest); // 400
        });

        // THEN: The quantity should remain unchanged
        var cart = await fixture.StubShoppingClient.GetCartAsync(cartId);
        cart.ShouldNotBeNull();
        cart.Items.Single().Quantity.ShouldBe(2); // Original quantity
    }

    [Fact]
    public async Task InitiateCheckout_ReturnsCheckoutId()
    {
        // GIVEN: A cart with items
        var customerId = Guid.NewGuid();
        var cartId = await fixture.CreateCartWithItemsAsync(customerId, ("DOG-BOWL-001", 2, 19.99m));

        // WHEN: The BFF initiates checkout
        var result = await fixture.Host.Scenario(scenario =>
        {
            scenario.Post.Url($"/api/storefront/carts/{cartId}/checkout");
            scenario.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        // THEN: A checkout ID should be returned
        var response = await result.ReadAsJsonAsync<InitiateCheckoutResponse>();
        response.ShouldNotBeNull();
        response.CheckoutId.ShouldNotBeNull();
        response.CheckoutId.Value.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task InitiateCheckout_WhenCartNotFound_Returns404()
    {
        // GIVEN: A cart ID that does not exist
        var nonExistentCartId = Guid.NewGuid();

        // WHEN: The BFF attempts to initiate checkout
        await fixture.Host.Scenario(scenario =>
        {
            scenario.Post.Url($"/api/storefront/carts/{nonExistentCartId}/checkout");
            scenario.StatusCodeShouldBe(HttpStatusCode.NotFound); // 404
        });
    }

    [Fact]
    public async Task InitiateCheckout_WhenCartEmpty_Returns400()
    {
        // GIVEN: An empty cart
        var customerId = Guid.NewGuid();
        var cartId = await fixture.StubShoppingClient.InitializeCartAsync(customerId);

        // WHEN: The BFF attempts to initiate checkout
        await fixture.Host.Scenario(scenario =>
        {
            scenario.Post.Url($"/api/storefront/carts/{cartId}/checkout");
            scenario.StatusCodeShouldBe(HttpStatusCode.BadRequest); // 400 - Cannot checkout empty cart
        });
    }

    private sealed record InitiateCheckoutResponse(Guid? CheckoutId);
}
