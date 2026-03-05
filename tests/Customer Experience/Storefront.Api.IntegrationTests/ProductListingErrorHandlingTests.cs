using Storefront.Composition;
using System.Net;

namespace Storefront.Api.IntegrationTests;

/// <summary>
/// Tests error handling for product listing queries
/// Verifies graceful degradation and proper HTTP status codes
/// </summary>
[Collection("Sequential")]
public class ProductListingErrorHandlingTests(TestFixture fixture) : IClassFixture<TestFixture>, IAsyncLifetime
{
    public Task InitializeAsync()
    {
        fixture.ClearAllStubs();
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetProductListing_WhenNoProductsMatchCategory_ReturnsEmptyList()
    {
        // GIVEN: The Product Catalog BC has products, but none in "Hamsters" category
        fixture.StubCatalogClient.AddProduct(new Storefront.Clients.ProductDto(
            "DOG-BOWL-001", "Dog Bowl", "Bowl", "Dogs", 19.99m, "Active",
            [new Storefront.Clients.ProductImageDto("https://example.com/bowl.jpg", "Bowl", 1)]));

        // WHEN: The BFF queries for products with non-matching category
        var result = await fixture.Host.Scenario(scenario =>
        {
            scenario.Get.Url("/api/storefront/products?category=Hamsters");
            scenario.StatusCodeShouldBe(HttpStatusCode.OK); // 200, not 404
        });

        // THEN: An empty product listing should be returned
        var listingView = await result.ReadAsJsonAsync<ProductListingView>();

        // AND: The listing should indicate no products found
        listingView.ShouldNotBeNull();
        listingView.Products.ShouldBeEmpty();
        listingView.TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task GetProductListing_WhenNegativePageNumber_UsesDefaultPage()
    {
        // GIVEN: The Product Catalog BC has products
        fixture.StubCatalogClient.AddProduct(new Storefront.Clients.ProductDto(
            "DOG-BOWL-001", "Dog Bowl", "Bowl", "Dogs", 19.99m, "Active",
            [new Storefront.Clients.ProductImageDto("https://example.com/bowl.jpg", "Bowl", 1)]));

        // WHEN: The BFF queries with negative page number
        var result = await fixture.Host.Scenario(scenario =>
        {
            scenario.Get.Url("/api/storefront/products?page=-1");
            scenario.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        // THEN: Default pagination should be applied (page 1)
        var listingView = await result.ReadAsJsonAsync<ProductListingView>();
        listingView.ShouldNotBeNull();
        listingView.Page.ShouldBeGreaterThanOrEqualTo(1); // Should default to page 1
    }

    [Fact]
    public async Task GetProductListing_WhenPageSizeTooLarge_LimitsToMaximum()
    {
        // GIVEN: The Product Catalog BC has products
        for (int i = 1; i <= 150; i++)
        {
            fixture.StubCatalogClient.AddProduct(new Storefront.Clients.ProductDto(
                $"SKU-{i:D3}", $"Product {i}", "Description", "Test", 10.00m, "Active",
                [new Storefront.Clients.ProductImageDto($"https://example.com/img{i}.jpg", "Image", 1)]));
        }

        // WHEN: The BFF queries with excessive page size (1000)
        var result = await fixture.Host.Scenario(scenario =>
        {
            scenario.Get.Url("/api/storefront/products?pageSize=1000");
            scenario.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        // THEN: Page size should be limited to maximum (100)
        var listingView = await result.ReadAsJsonAsync<ProductListingView>();
        listingView.ShouldNotBeNull();
        listingView.PageSize.ShouldBeLessThanOrEqualTo(100); // Max page size
        listingView.Products.Count.ShouldBeLessThanOrEqualTo(100);
    }

    [Fact]
    public async Task GetProductListing_WhenPageNumberExceedsTotalPages_ReturnsEmptyList()
    {
        // GIVEN: The Product Catalog BC has 10 products
        for (int i = 1; i <= 10; i++)
        {
            fixture.StubCatalogClient.AddProduct(new Storefront.Clients.ProductDto(
                $"SKU-{i:D3}", $"Product {i}", "Description", "Test", 10.00m, "Active",
                [new Storefront.Clients.ProductImageDto($"https://example.com/img{i}.jpg", "Image", 1)]));
        }

        // WHEN: The BFF queries for page 100 (way beyond available data)
        var result = await fixture.Host.Scenario(scenario =>
        {
            scenario.Get.Url("/api/storefront/products?page=100&pageSize=20");
            scenario.StatusCodeShouldBe(HttpStatusCode.OK); // 200, not 404
        });

        // THEN: An empty list should be returned (not an error)
        var listingView = await result.ReadAsJsonAsync<ProductListingView>();
        listingView.ShouldNotBeNull();
        listingView.Products.ShouldBeEmpty();
        listingView.TotalCount.ShouldBeGreaterThan(0); // Total count still accurate
    }

    [Fact]
    public async Task GetProductListing_WhenCatalogBCEmpty_ReturnsEmptyList()
    {
        // GIVEN: The Product Catalog BC has NO products (stub is empty)

        // WHEN: The BFF queries for products
        var result = await fixture.Host.Scenario(scenario =>
        {
            scenario.Get.Url("/api/storefront/products");
            scenario.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        // THEN: An empty product listing should be returned
        var listingView = await result.ReadAsJsonAsync<ProductListingView>();
        listingView.ShouldNotBeNull();
        listingView.Products.ShouldBeEmpty();
        listingView.TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task GetProductListing_WhenInvalidCategoryName_ReturnsEmptyList()
    {
        // GIVEN: The Product Catalog BC has products
        fixture.StubCatalogClient.AddProduct(new Storefront.Clients.ProductDto(
            "DOG-BOWL-001", "Dog Bowl", "Bowl", "Dogs", 19.99m, "Active",
            [new Storefront.Clients.ProductImageDto("https://example.com/bowl.jpg", "Bowl", 1)]));

        // WHEN: The BFF queries with special characters in category
        var result = await fixture.Host.Scenario(scenario =>
        {
            scenario.Get.Url("/api/storefront/products?category=<script>alert('xss')</script>");
            scenario.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        // THEN: Should handle gracefully (empty list, not XSS or error)
        var listingView = await result.ReadAsJsonAsync<ProductListingView>();
        listingView.ShouldNotBeNull();
        listingView.Products.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetProductListing_WithZeroPageSize_UsesDefaultPageSize()
    {
        // GIVEN: The Product Catalog BC has products
        fixture.StubCatalogClient.AddProduct(new Storefront.Clients.ProductDto(
            "DOG-BOWL-001", "Dog Bowl", "Bowl", "Dogs", 19.99m, "Active",
            [new Storefront.Clients.ProductImageDto("https://example.com/bowl.jpg", "Bowl", 1)]));

        // WHEN: The BFF queries with zero page size
        var result = await fixture.Host.Scenario(scenario =>
        {
            scenario.Get.Url("/api/storefront/products?pageSize=0");
            scenario.StatusCodeShouldBe(HttpStatusCode.OK);
        });

        // THEN: Default page size should be used
        var listingView = await result.ReadAsJsonAsync<ProductListingView>();
        listingView.ShouldNotBeNull();
        listingView.PageSize.ShouldBeGreaterThan(0); // Default to 20
    }
}
