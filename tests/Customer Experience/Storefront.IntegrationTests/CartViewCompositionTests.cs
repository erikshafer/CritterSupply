using Alba;
using Shouldly;
using Storefront.Composition;
using System.Net;
using System.Net.Http.Json;

namespace Storefront.IntegrationTests;

/// <summary>
/// BDD-style tests for Cart View composition
/// Verifies BFF aggregates Shopping BC + Catalog BC correctly
/// </summary>
[Collection("Storefront Integration Tests")]
public class CartViewCompositionTests : IClassFixture<TestFixture>
{
    private readonly TestFixture _fixture;

    public CartViewCompositionTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetCartView_WhenCartExists_ComposesDataFromShoppingAndCatalogBCs()
    {
        // GIVEN: A customer has an active cart with items
        var customerId = Guid.NewGuid();
        var cartId = Guid.NewGuid();

        // AND: Shopping BC has cart data (stubbed)
        _fixture.StubShoppingClient.AddCart(cartId, customerId,
            new Storefront.Clients.CartItemDto("DOG-BOWL-01", 2, 19.99m),
            new Storefront.Clients.CartItemDto("CAT-TOY-05", 1, 29.99m));

        // AND: Catalog BC has product data (stubbed)
        _fixture.StubCatalogClient.AddProduct(new Storefront.Clients.ProductDto(
            "DOG-BOWL-01",
            "Ceramic Dog Bowl (Large)",
            "High-quality ceramic dog bowl",
            "Dogs",
            19.99m,
            "Active",
            new List<Storefront.Clients.ProductImageDto>
            {
                new("https://example.com/dog-bowl.jpg", "Ceramic Dog Bowl", 1)
            }));

        _fixture.StubCatalogClient.AddProduct(new Storefront.Clients.ProductDto(
            "CAT-TOY-05",
            "Interactive Cat Laser",
            "Fun laser toy for cats",
            "Cats",
            29.99m,
            "Active",
            new List<Storefront.Clients.ProductImageDto>
            {
                new("https://example.com/cat-laser.jpg", "Cat Laser", 1)
            }));

        // WHEN: The BFF queries for the cart view
        var result = await _fixture.Host.Scenario(scenario =>
        {
            scenario.Get.Url($"/api/storefront/carts/{cartId}");
            scenario.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        // THEN: The composed view should be returned
        var cartView = await result.ReadAsJsonAsync<CartView>();

        // AND: The cart view should contain data from Shopping BC
        cartView.ShouldNotBeNull();
        cartView.CartId.ShouldBe(cartId);
        cartView.CustomerId.ShouldBe(customerId);

        // AND: Line items should be enriched with product details from Catalog BC
        cartView.Items.ShouldNotBeEmpty();
        cartView.Items.Count.ShouldBe(2);

        var dogBowl = cartView.Items.First(i => i.Sku == "DOG-BOWL-01");
        dogBowl.ProductName.ShouldBe("Ceramic Dog Bowl (Large)"); // From Catalog BC
        dogBowl.ProductImageUrl.ShouldBe("https://example.com/dog-bowl.jpg"); // From Catalog BC
        dogBowl.Quantity.ShouldBe(2);
        dogBowl.UnitPrice.ShouldBe(19.99m);
        dogBowl.LineTotal.ShouldBe(39.98m);

        // AND: Subtotal should be calculated correctly
        var expectedSubtotal = (2 * 19.99m) + (1 * 29.99m); // $69.97
        cartView.Subtotal.ShouldBe(expectedSubtotal);
    }

    [Fact]
    public async Task GetCartView_WhenCartDoesNotExist_Returns404()
    {
        // GIVEN: A cart ID that does not exist
        var nonExistentCartId = Guid.NewGuid();

        // WHEN: The BFF queries for the cart view
        var result = await _fixture.Host.Scenario(scenario =>
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
        _fixture.StubShoppingClient.AddCart(cartId, customerId,
            new Storefront.Clients.CartItemDto("UNKNOWN-SKU-999", 1, 15.00m));

        // AND: Catalog BC does NOT have product "UNKNOWN-SKU-999" (stub returns null)

        // WHEN: The BFF queries for the cart view
        var result = await _fixture.Host.Scenario(scenario =>
        {
            scenario.Get.Url($"/api/storefront/carts/{cartId}");
            scenario.StatusCodeShouldBe(HttpStatusCode.OK);
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
}
