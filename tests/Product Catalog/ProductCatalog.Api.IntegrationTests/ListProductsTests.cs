using Alba;
using ProductCatalog.Api.Products;
using ProductCatalog.Products;
using Shouldly;

namespace ProductCatalog.IntegrationTests;

public sealed class ListProductsTests : IClassFixture<ProductCatalogFixture>
{
    private readonly ProductCatalogFixture _fixture;

    public ListProductsTests(ProductCatalogFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CanListAllProducts()
    {
        // Act
        var response = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url("/api/products");
            s.StatusCodeShouldBeOk();
        });

        // Assert
        var result = response.ReadAsJson<ProductListResult>();
        result.ShouldNotBeNull();
        result.Products.ShouldNotBeEmpty();
        result.TotalCount.ShouldBeGreaterThan(0);
        result.Page.ShouldBe(1);
        result.PageSize.ShouldBe(20);
    }

    [Fact]
    public async Task CanListProductsWithPagination()
    {
        // Act - Get page 2 with 5 products per page
        var response = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url("/api/products?page=2&pageSize=5");
            s.StatusCodeShouldBeOk();
        });

        // Assert
        var result = response.ReadAsJson<ProductListResult>();
        result.Page.ShouldBe(2);
        result.PageSize.ShouldBe(5);
        result.Products.Count.ShouldBeLessThanOrEqualTo(5);
    }

    [Fact]
    public async Task CanFilterProductsByCategory()
    {
        // Act
        var response = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url("/api/products?category=Dogs");
            s.StatusCodeShouldBeOk();
        });

        // Assert
        var result = response.ReadAsJson<ProductListResult>();
        result.Products.ShouldNotBeEmpty();
        result.Products.ShouldAllBe(p => p.Category.Value == "Dogs");
    }

    [Fact]
    public async Task CanFilterProductsByStatus()
    {
        // Act
        var response = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url("/api/products?status=Active");
            s.StatusCodeShouldBeOk();
        });

        // Assert
        var result = response.ReadAsJson<ProductListResult>();
        result.Products.ShouldNotBeEmpty();
        result.Products.ShouldAllBe(p => p.Status == ProductStatus.Active);
    }

    [Fact]
    public async Task CanFilterProductsByCategoryAndStatus()
    {
        // Act
        var response = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url("/api/products?category=Cats&status=Active");
            s.StatusCodeShouldBeOk();
        });

        // Assert
        var result = response.ReadAsJson<ProductListResult>();
        result.Products.ShouldNotBeEmpty();
        result.Products.ShouldAllBe(p => p.Category.Value == "Cats" && p.Status == ProductStatus.Active);
    }

    [Fact]
    public async Task ListProducts_ReturnsEmptyListForNonExistentCategory()
    {
        // Act
        var response = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url("/api/products?category=NonExistentCategory");
            s.StatusCodeShouldBeOk();
        });

        // Assert
        var result = response.ReadAsJson<ProductListResult>();
        result.Products.ShouldBeEmpty();
        result.TotalCount.ShouldBe(0);
    }
}
