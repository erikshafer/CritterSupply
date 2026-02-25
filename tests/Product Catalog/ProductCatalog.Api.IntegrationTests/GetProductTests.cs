using ProductCatalog.Products;
using Shouldly;

namespace ProductCatalog.Api.IntegrationTests;

[Collection(IntegrationTestCollection.Name)]
public sealed class GetProductTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public GetProductTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.CleanAllDocumentsAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task CanGetProductBySku()
    {
        // Arrange - Seed test product first
        var createProduct = Product.Create(
            "DOG-BOWL-001",
            "Premium Stainless Steel Dog Bowl",
            "Stainless steel dog bowl, dishwasher safe",
            "Dogs");

        using (var session = _fixture.GetDocumentSession())
        {
            session.Store(createProduct);
            await session.SaveChangesAsync();
        }

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
        // Arrange - Seed test product with images
        var createProduct = Product.Create(
            "DOG-BOWL-001",
            "Premium Stainless Steel Dog Bowl",
            "Stainless steel dog bowl, dishwasher safe",
            "Dogs",
            images: new[]
            {
                ProductImage.Create("https://placeholder.com/dog-bowl-001.jpg", "Premium dog bowl image", 0)
            }.ToList().AsReadOnly());

        using (var session = _fixture.GetDocumentSession())
        {
            session.Store(createProduct);
            await session.SaveChangesAsync();
        }

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
        // Arrange - Seed test product with dimensions
        var createProduct = Product.Create(
            "DOG-BOWL-001",
            "Premium Stainless Steel Dog Bowl",
            "Stainless steel dog bowl, dishwasher safe",
            "Dogs",
            dimensions: ProductDimensions.Create(8.5m, 8.5m, 3.0m, 1.2m));

        using (var session = _fixture.GetDocumentSession())
        {
            session.Store(createProduct);
            await session.SaveChangesAsync();
        }

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
