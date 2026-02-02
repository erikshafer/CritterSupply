using ProductCatalog.Products;
using Shouldly;

namespace ProductCatalog.Api.IntegrationTests;

[Collection(IntegrationTestCollection.Name)]
public sealed class GetProductTests : IClassFixture<TestFixture>
{
    private readonly TestFixture _fixture;

    public GetProductTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CanGetProductBySku()
    {
        // Act
        var product = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url("/api/products/DOG-BOWL-001");
            s.StatusCodeShouldBeOk();
        });

        // Assert
        var result = product.ReadAsJson<Product>();
        result.ShouldNotBeNull();
        result.Sku.Value.ShouldBe("DOG-BOWL-001");
        result.Name.Value.ShouldBe("Premium Stainless Steel Dog Bowl");
        result.Status.ShouldBe(ProductStatus.Active);
    }

    [Fact]
    public async Task GetProduct_Returns404ForNonExistentSku()
    {
        // Act & Assert
        await _fixture.Host.Scenario(s =>
        {
            s.Get.Url("/api/products/NONEXISTENT-SKU");
            s.StatusCodeShouldBe(404);
        });
    }

    [Fact]
    public async Task GetProduct_ReturnsProductWithImages()
    {
        // Act
        var product = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url("/api/products/DOG-BOWL-001");
            s.StatusCodeShouldBeOk();
        });

        // Assert
        var result = product.ReadAsJson<Product>();
        result.Images.ShouldNotBeEmpty();
        result.Images[0].Url.ShouldContain("placeholder");
        result.Images[0].AltText.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GetProduct_ReturnsProductWithDimensions()
    {
        // Act
        var product = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url("/api/products/DOG-BOWL-001");
            s.StatusCodeShouldBeOk();
        });

        // Assert
        var result = product.ReadAsJson<Product>();
        result.Dimensions.ShouldNotBeNull();
        result.Dimensions.Length.ShouldBeGreaterThan(0);
        result.Dimensions.Weight.ShouldBeGreaterThan(0);
    }
}
