using Alba;
using Shouldly;
using Storefront.Composition;
using System.Net;
using System.Net.Http.Json;

namespace Storefront.IntegrationTests;

/// <summary>
/// BDD-style tests for Checkout View composition
/// Verifies BFF aggregates Orders BC + Customer Identity BC + Catalog BC correctly
///
/// Maps to Gherkin scenario: "Checkout page composes data from multiple BCs"
/// (checkout-flow.feature line 251)
/// </summary>
[Collection("Storefront Integration Tests")]
public class CheckoutViewCompositionTests : IClassFixture<TestFixture>
{
    private readonly TestFixture _fixture;

    public CheckoutViewCompositionTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetCheckoutView_WhenCheckoutExists_ComposesDataFromOrdersCustomerIdentityAndCatalogBCs()
    {
        // GIVEN: A customer has an active checkout
        var customerId = Guid.NewGuid();
        var checkoutId = Guid.NewGuid();

        // AND: Orders BC has checkout data with line items
        _fixture.StubOrdersClient.AddCheckout(checkoutId, customerId,
            new Storefront.Clients.CheckoutItemDto("DOG-BOWL-01", 2, 19.99m));

        // AND: Customer Identity BC has saved shipping addresses
        _fixture.StubCustomerIdentityClient.AddAddress(new Storefront.Clients.CustomerAddressDto(
            Guid.NewGuid(),
            customerId,
            "Home",
            "123 Main St",
            "",
            "Seattle",
            "WA",
            "98101",
            "US",
            "Shipping",
            true));

        // AND: Catalog BC has product data for enrichment
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

        // WHEN: The BFF queries for the checkout view
        var result = await _fixture.Host.Scenario(scenario =>
        {
            scenario.Get.Url($"/api/storefront/checkouts/{checkoutId}");
            scenario.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        // THEN: The composed view should be returned
        var checkoutView = await result.ReadAsJsonAsync<CheckoutView>();

        // AND: The checkout view should contain data from Orders BC
        checkoutView.ShouldNotBeNull();
        checkoutView.CheckoutId.ShouldBe(checkoutId);
        checkoutView.CustomerId.ShouldBe(customerId);
        checkoutView.CurrentStep.ShouldBe(CheckoutStep.ShippingAddress);

        // AND: Line items should be enriched with product details from Catalog BC
        checkoutView.Items.ShouldNotBeEmpty();

        var firstItem = checkoutView.Items.First();
        firstItem.ProductName.ShouldBe("Ceramic Dog Bowl (Large)"); // From Catalog BC
        firstItem.ProductImageUrl.ShouldBe("https://example.com/dog-bowl.jpg"); // From Catalog BC

        // AND: Saved addresses should be loaded from Customer Identity BC
        checkoutView.SavedAddresses.ShouldNotBeEmpty();

        var firstAddress = checkoutView.SavedAddresses.First();
        firstAddress.AddressId.ShouldNotBe(Guid.Empty);
        firstAddress.Nickname.ShouldBe("Home");
        firstAddress.DisplayLine.ShouldContain(","); // "123 Main St, Seattle, WA 98101"

        // AND: Pricing calculations should be correct
        checkoutView.Subtotal.ShouldBe(39.98m);
        checkoutView.ShippingCost.ShouldBe(5.99m);
        checkoutView.Total.ShouldBe(checkoutView.Subtotal + checkoutView.ShippingCost);

        // AND: The checkout should indicate if the user can proceed
        checkoutView.CanProceedToNextStep.ShouldBeTrue();
    }

    [Fact]
    public async Task GetCheckoutView_WhenCustomerHasNoSavedAddresses_ReturnsEmptyAddressList()
    {
        // GIVEN: A customer has an active checkout but no saved addresses
        var customerId = Guid.NewGuid();
        var checkoutId = Guid.NewGuid();

        // AND: Orders BC has checkout data with line items
        _fixture.StubOrdersClient.AddCheckout(checkoutId, customerId,
            new Storefront.Clients.CheckoutItemDto("CAT-TOY-05", 1, 29.99m));

        // AND: Catalog BC has product data
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

        // AND: Customer Identity BC returns empty address list (no AddAddress calls)

        // WHEN: The BFF queries for the checkout view
        var result = await _fixture.Host.Scenario(scenario =>
        {
            scenario.Get.Url($"/api/storefront/checkouts/{checkoutId}");
            scenario.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        // THEN: The composed view should be returned
        var checkoutView = await result.ReadAsJsonAsync<CheckoutView>();
        checkoutView.ShouldNotBeNull();

        // AND: The saved addresses list should be empty
        checkoutView.SavedAddresses.ShouldBeEmpty();

        // AND: The user can still proceed (to add a new address)
        checkoutView.CanProceedToNextStep.ShouldBeTrue();
    }

    [Fact]
    public async Task GetCheckoutView_FiltersOnlyShippingAddresses()
    {
        // GIVEN: A customer has both shipping and billing addresses saved
        var customerId = Guid.NewGuid();
        var checkoutId = Guid.NewGuid();

        // AND: Orders BC has checkout data
        _fixture.StubOrdersClient.AddCheckout(checkoutId, customerId,
            new Storefront.Clients.CheckoutItemDto("DOG-FOOD-99", 1, 45.00m));

        // AND: Catalog BC has product data
        _fixture.StubCatalogClient.AddProduct(new Storefront.Clients.ProductDto(
            "DOG-FOOD-99",
            "Premium Dog Food",
            "Nutritious dog food",
            "Dogs",
            45.00m,
            "Active",
            new List<Storefront.Clients.ProductImageDto>
            {
                new("https://example.com/dog-food.jpg", "Dog Food", 1)
            }));

        // AND: Customer Identity BC has 2 shipping addresses and 1 billing address
        _fixture.StubCustomerIdentityClient.AddAddress(new Storefront.Clients.CustomerAddressDto(
            Guid.NewGuid(), customerId, "Home", "123 Main St", "", "Seattle", "WA", "98101", "US", "Shipping", true));
        _fixture.StubCustomerIdentityClient.AddAddress(new Storefront.Clients.CustomerAddressDto(
            Guid.NewGuid(), customerId, "Work", "456 Office Blvd", "", "Seattle", "WA", "98102", "US", "Shipping", false));
        _fixture.StubCustomerIdentityClient.AddAddress(new Storefront.Clients.CustomerAddressDto(
            Guid.NewGuid(), customerId, "Billing", "789 Billing Ave", "", "Seattle", "WA", "98103", "US", "Billing", false));

        // WHEN: The BFF queries for the checkout view
        var result = await _fixture.Host.Scenario(scenario =>
        {
            scenario.Get.Url($"/api/storefront/checkouts/{checkoutId}");
            scenario.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        // THEN: The composed view should be returned
        var checkoutView = await result.ReadAsJsonAsync<CheckoutView>();

        // AND: Only shipping addresses should be included (billing filtered out)
        // Note: This assumes the BFF passes addressType="Shipping" to Customer Identity BC
        checkoutView.SavedAddresses.ShouldNotBeEmpty();

        // All addresses should be shipping addresses (2 shipping, 0 billing)
        checkoutView.SavedAddresses.Count.ShouldBe(2);
    }
}
