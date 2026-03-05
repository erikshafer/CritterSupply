using Storefront.Composition;
using System.Net;

namespace Storefront.Api.IntegrationTests;

/// <summary>
/// Tests error handling for cart view queries
/// Verifies graceful degradation and proper HTTP status codes
/// </summary>
[Collection("Sequential")]
public class CartViewErrorHandlingTests(TestFixture fixture) : IClassFixture<TestFixture>, IAsyncLifetime
{
    public Task InitializeAsync()
    {
        fixture.ClearAllStubs();
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetCartView_WhenCartDoesNotExist_Returns404()
    {
        // GIVEN: A cart ID that does not exist in Shopping BC
        var nonExistentCartId = Guid.NewGuid();

        // WHEN: The BFF queries for the cart view
        var result = await fixture.Host.Scenario(scenario =>
        {
            scenario.Get.Url($"/api/storefront/carts/{nonExistentCartId}");
            scenario.StatusCodeShouldBe(HttpStatusCode.NotFound);
        });

        // THEN: A 404 Not Found response should be returned
        result.Context.Response.StatusCode.ShouldBe((int)HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetCartView_WhenProductNotFoundInCatalog_UsesUnknownProductPlaceholder()
    {
        // GIVEN: A cart with an item whose SKU is not in Catalog BC
        var customerId = Guid.NewGuid();
        var cartId = Guid.NewGuid();

        // AND: Shopping BC returns cart with SKU "UNKNOWN-SKU-999"
        fixture.StubShoppingClient.AddCart(cartId, customerId,
            new Storefront.Clients.CartItemDto("UNKNOWN-SKU-999", 1, 15.00m));

        // AND: Catalog BC does NOT have product "UNKNOWN-SKU-999" (stub returns null by default)

        // WHEN: The BFF queries for the cart view
        var result = await fixture.Host.Scenario(scenario =>
        {
            scenario.Get.Url($"/api/storefront/carts/{cartId}");
            scenario.StatusCodeShouldBe(HttpStatusCode.OK); // Should succeed gracefully
        });

        // THEN: The composed view should still be returned
        var cartView = await result.ReadAsJsonAsync<CartView>();
        cartView.ShouldNotBeNull();

        // AND: The missing product should have a placeholder name
        var itemWithMissingProduct = cartView.Items.FirstOrDefault(i => i.Sku == "UNKNOWN-SKU-999");
        itemWithMissingProduct.ShouldNotBeNull();
        itemWithMissingProduct.ProductName.ShouldBe("Unknown Product");
        itemWithMissingProduct.ProductImageUrl.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetCartView_WhenCartIsEmpty_ReturnsValidViewWithZeroItems()
    {
        // GIVEN: A cart with no items
        var customerId = Guid.NewGuid();
        var cartId = Guid.NewGuid();

        fixture.StubShoppingClient.AddCart(cartId, customerId); // No items

        // WHEN: The BFF queries for the cart view
        var result = await fixture.Host.Scenario(scenario =>
        {
            scenario.Get.Url($"/api/storefront/carts/{cartId}");
            scenario.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        // THEN: A valid cart view should be returned
        var cartView = await result.ReadAsJsonAsync<CartView>();
        cartView.ShouldNotBeNull();
        cartView.CartId.ShouldBe(cartId);
        cartView.CustomerId.ShouldBe(customerId);

        // AND: The cart should have zero items
        cartView.Items.ShouldBeEmpty();
        cartView.Subtotal.ShouldBe(0m);
    }

    [Fact]
    public async Task GetCartView_WhenMultipleProductsMissing_HandlesAllGracefully()
    {
        // GIVEN: A cart with 3 items, 2 of which are not in Catalog BC
        var customerId = Guid.NewGuid();
        var cartId = Guid.NewGuid();

        fixture.StubShoppingClient.AddCart(cartId, customerId,
            new Storefront.Clients.CartItemDto("UNKNOWN-001", 1, 10.00m),
            new Storefront.Clients.CartItemDto("DOG-BOWL-001", 2, 19.99m),
            new Storefront.Clients.CartItemDto("UNKNOWN-002", 1, 15.00m));

        // AND: Only DOG-BOWL-001 exists in Catalog BC
        fixture.StubCatalogClient.AddProduct(new Storefront.Clients.ProductDto(
            "DOG-BOWL-001",
            "Ceramic Dog Bowl",
            "Bowl",
            "Dogs",
            19.99m,
            "Active",
            [new Storefront.Clients.ProductImageDto("https://example.com/bowl.jpg", "Bowl", 1)]));

        // WHEN: The BFF queries for the cart view
        var result = await fixture.Host.Scenario(scenario =>
        {
            scenario.Get.Url($"/api/storefront/carts/{cartId}");
            scenario.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        // THEN: All items should be in the view
        var cartView = await result.ReadAsJsonAsync<CartView>();
        cartView.Items.Count.ShouldBe(3);

        // AND: Unknown products should have placeholder names
        cartView.Items.Where(i => i.ProductName == "Unknown Product").Count().ShouldBe(2);

        // AND: Known product should have correct data
        var knownProduct = cartView.Items.FirstOrDefault(i => i.Sku == "DOG-BOWL-001");
        knownProduct.ShouldNotBeNull();
        knownProduct.ProductName.ShouldBe("Ceramic Dog Bowl");
    }

    [Fact]
    public async Task GetCartView_CalculatesSubtotalCorrectly_EvenWithMissingProducts()
    {
        // GIVEN: A cart with items (some products missing from Catalog)
        var customerId = Guid.NewGuid();
        var cartId = Guid.NewGuid();

        fixture.StubShoppingClient.AddCart(cartId, customerId,
            new Storefront.Clients.CartItemDto("UNKNOWN-001", 2, 10.00m),
            new Storefront.Clients.CartItemDto("UNKNOWN-002", 1, 25.00m));

        // WHEN: The BFF queries for the cart view
        var result = await fixture.Host.Scenario(scenario =>
        {
            scenario.Get.Url($"/api/storefront/carts/{cartId}");
            scenario.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        // THEN: Subtotal should be calculated from cart data (not catalog data)
        var cartView = await result.ReadAsJsonAsync<CartView>();
        var expectedSubtotal = (2 * 10.00m) + (1 * 25.00m); // 45.00
        cartView.Subtotal.ShouldBe(expectedSubtotal);
    }
}
