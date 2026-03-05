using Storefront.Composition;
using System.Net;

namespace Storefront.Api.IntegrationTests;

/// <summary>
/// Tests realistic multi-step cart workflows (integration scenarios)
/// Simulates user journeys through cart operations
/// </summary>
[Collection("Sequential")]
public class CartWorkflowTests(TestFixture fixture) : IClassFixture<TestFixture>, IAsyncLifetime
{
    public Task InitializeAsync()
    {
        fixture.ClearAllStubs();
        fixture.SeedCommonProducts(); // Seed test products for cart operations
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task HappyPath_InitializeAddChangeRemoveVerify()
    {
        // GIVEN: A customer
        var customerId = Guid.NewGuid();

        // WHEN: Customer initializes a cart
        var initResult = await fixture.Host.Scenario(scenario =>
        {
            scenario.Post.Json(new { customerId }).ToUrl("/api/storefront/carts/initialize");
            scenario.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        var cartId = await initResult.ReadAsJsonAsync<Guid>();
        cartId.ShouldNotBe(Guid.Empty);

        // AND: Customer adds 3 items
        await fixture.Host.Scenario(scenario =>
        {
            scenario.Post.Json(new { sku = "DOG-BOWL-001", quantity = 2, unitPrice = 19.99m })
                .ToUrl($"/api/storefront/carts/{cartId}/items");
            scenario.StatusCodeShouldBe(HttpStatusCode.NoContent);
        });

        await fixture.Host.Scenario(scenario =>
        {
            scenario.Post.Json(new { sku = "CAT-TOY-001", quantity = 1, unitPrice = 29.99m })
                .ToUrl($"/api/storefront/carts/{cartId}/items");
            scenario.StatusCodeShouldBe(HttpStatusCode.NoContent);
        });

        await fixture.Host.Scenario(scenario =>
        {
            scenario.Post.Json(new { sku = "DOG-FOOD-001", quantity = 1, unitPrice = 45.00m })
                .ToUrl($"/api/storefront/carts/{cartId}/items");
            scenario.StatusCodeShouldBe(HttpStatusCode.NoContent);
        });

        // AND: Customer changes quantity of DOG-BOWL-001 from 2 to 5
        await fixture.Host.Scenario(scenario =>
        {
            scenario.Put.Json(new { newQuantity = 5 })
                .ToUrl($"/api/storefront/carts/{cartId}/items/DOG-BOWL-001/quantity");
            scenario.StatusCodeShouldBe(HttpStatusCode.NoContent);
        });

        // AND: Customer removes CAT-TOY-001
        await fixture.Host.Scenario(scenario =>
        {
            scenario.Delete.Url($"/api/storefront/carts/{cartId}/items/CAT-TOY-001");
            scenario.StatusCodeShouldBe(HttpStatusCode.NoContent);
        });

        // THEN: Verify final cart state via CartView
        var cartViewResult = await fixture.Host.Scenario(scenario =>
        {
            scenario.Get.Url($"/api/storefront/carts/{cartId}");
            scenario.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        var cartView = await cartViewResult.ReadAsJsonAsync<CartView>();

        // AND: Cart should have 2 items (DOG-BOWL-001 qty 5, DOG-FOOD-001 qty 1)
        cartView.ShouldNotBeNull();
        cartView.Items.Count.ShouldBe(2);

        var dogBowl = cartView.Items.FirstOrDefault(i => i.Sku == "DOG-BOWL-001");
        dogBowl.ShouldNotBeNull();
        dogBowl.Quantity.ShouldBe(5);
        dogBowl.LineTotal.ShouldBe(99.95m); // 5 * 19.99

        var dogFood = cartView.Items.FirstOrDefault(i => i.Sku == "DOG-FOOD-001");
        dogFood.ShouldNotBeNull();
        dogFood.Quantity.ShouldBe(1);
        dogFood.LineTotal.ShouldBe(45.00m);

        // AND: Subtotal should be correct
        var expectedSubtotal = 99.95m + 45.00m; // 144.95
        cartView.Subtotal.ShouldBe(expectedSubtotal);
    }

    [Fact]
    public async Task AddSameSkuTwice_IncrementsQuantity()
    {
        // GIVEN: A cart with an item (DOG-BOWL-001 qty 2)
        var customerId = Guid.NewGuid();
        var cartId = await fixture.CreateCartWithItemsAsync(customerId, ("DOG-BOWL-001", 2, 19.99m));

        // WHEN: Customer adds the same SKU again (qty 3)
        await fixture.Host.Scenario(scenario =>
        {
            scenario.Post.Json(new { sku = "DOG-BOWL-001", quantity = 3, unitPrice = 19.99m })
                .ToUrl($"/api/storefront/carts/{cartId}/items");
            scenario.StatusCodeShouldBe(HttpStatusCode.NoContent);
        });

        // THEN: Quantity should be incremented (not duplicate line item)
        var cart = await fixture.StubShoppingClient.GetCartAsync(cartId);
        cart.ShouldNotBeNull();
        cart.Items.Count.ShouldBe(1); // Still 1 line item

        var item = cart.Items.Single();
        item.Sku.ShouldBe("DOG-BOWL-001");
        item.Quantity.ShouldBe(5); // 2 + 3 = 5
    }

    [Fact]
    public async Task AddThenRemoveItem_ResultsInEmptyCart()
    {
        // GIVEN: A customer initializes a cart
        var customerId = Guid.NewGuid();
        var initResult = await fixture.Host.Scenario(scenario =>
        {
            scenario.Post.Json(new { customerId }).ToUrl("/api/storefront/carts/initialize");
            scenario.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        var cartId = await initResult.ReadAsJsonAsync<Guid>();

        // WHEN: Customer adds an item
        await fixture.Host.Scenario(scenario =>
        {
            scenario.Post.Json(new { sku = "DOG-BOWL-001", quantity = 1, unitPrice = 19.99m })
                .ToUrl($"/api/storefront/carts/{cartId}/items");
            scenario.StatusCodeShouldBe(HttpStatusCode.NoContent);
        });

        // AND: Customer removes the item
        await fixture.Host.Scenario(scenario =>
        {
            scenario.Delete.Url($"/api/storefront/carts/{cartId}/items/DOG-BOWL-001");
            scenario.StatusCodeShouldBe(HttpStatusCode.NoContent);
        });

        // THEN: Cart should be empty
        var cart = await fixture.StubShoppingClient.GetCartAsync(cartId);
        cart.ShouldNotBeNull();
        cart.Items.ShouldBeEmpty();

        // AND: CartView should reflect empty cart
        var cartViewResult = await fixture.Host.Scenario(scenario =>
        {
            scenario.Get.Url($"/api/storefront/carts/{cartId}");
            scenario.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        var cartView = await cartViewResult.ReadAsJsonAsync<CartView>();
        cartView.Items.ShouldBeEmpty();
        cartView.Subtotal.ShouldBe(0m);
    }

    [Fact]
    public async Task InitializeCartTwiceForSameCustomer_CreatesSeparateCarts()
    {
        // GIVEN: A customer ID
        var customerId = Guid.NewGuid();

        // WHEN: Customer initializes a cart twice
        var result1 = await fixture.Host.Scenario(scenario =>
        {
            scenario.Post.Json(new { customerId }).ToUrl("/api/storefront/carts/initialize");
            scenario.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        var result2 = await fixture.Host.Scenario(scenario =>
        {
            scenario.Post.Json(new { customerId }).ToUrl("/api/storefront/carts/initialize");
            scenario.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        var cartId1 = await result1.ReadAsJsonAsync<Guid>();
        var cartId2 = await result2.ReadAsJsonAsync<Guid>();

        // THEN: Two separate cart IDs should be returned
        cartId1.ShouldNotBe(cartId2);

        // AND: Both carts should exist
        var cart1 = await fixture.StubShoppingClient.GetCartAsync(cartId1);
        var cart2 = await fixture.StubShoppingClient.GetCartAsync(cartId2);

        cart1.ShouldNotBeNull();
        cart2.ShouldNotBeNull();
        cart1.CustomerId.ShouldBe(customerId);
        cart2.CustomerId.ShouldBe(customerId);
    }
}
