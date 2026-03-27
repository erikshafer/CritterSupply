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
        // Arrange - Seed via event sourcing so ProductCatalogView is populated
        using (var session = _fixture.GetDocumentSession())
        {
            var productId = Guid.NewGuid();
            session.Events.StartStream<CatalogProduct>(productId, new ProductCreated(
                ProductId: productId, Sku: "DOG-BOWL-001",
                Name: "Premium Stainless Steel Dog Bowl",
                Description: "Stainless steel dog bowl, dishwasher safe",
                Category: "Dogs", CreatedAt: DateTimeOffset.UtcNow));
            await session.SaveChangesAsync();
        }

        // Act
        var product = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url("/api/products/DOG-BOWL-001");
            s.StatusCodeShouldBeOk();
        });

        // Assert
        var result = product.ReadAsJson<ProductCatalogView>();
        result.ShouldNotBeNull();
        result!.Sku.ShouldBe("DOG-BOWL-001");
        result.Name.ShouldBe("Premium Stainless Steel Dog Bowl");
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
        // Arrange - Seed via event sourcing with images
        using (var session = _fixture.GetDocumentSession())
        {
            var productId = Guid.NewGuid();
            session.Events.StartStream<CatalogProduct>(productId, new ProductCreated(
                ProductId: productId, Sku: "DOG-BOWL-001",
                Name: "Premium Stainless Steel Dog Bowl",
                Description: "Stainless steel dog bowl, dishwasher safe",
                Category: "Dogs",
                Images: new[]
                {
                    ProductImage.Create("https://placeholder.com/dog-bowl-001.jpg", "Premium dog bowl image", 0)
                }.ToList().AsReadOnly(),
                CreatedAt: DateTimeOffset.UtcNow));
            await session.SaveChangesAsync();
        }

        // Act
        var product = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url("/api/products/DOG-BOWL-001");
            s.StatusCodeShouldBeOk();
        });

        // Assert
        var result = product.ReadAsJson<ProductCatalogView>();
        result.ShouldNotBeNull();
        result!.Images.ShouldNotBeEmpty();
        result.Images[0].Url.ShouldContain("placeholder");
        result.Images[0].AltText.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GetProduct_ReturnsProductWithDimensions()
    {
        // Arrange - Seed via event sourcing with dimensions
        using (var session = _fixture.GetDocumentSession())
        {
            var productId = Guid.NewGuid();
            session.Events.StartStream<CatalogProduct>(productId, new ProductCreated(
                ProductId: productId, Sku: "DOG-BOWL-001",
                Name: "Premium Stainless Steel Dog Bowl",
                Description: "Stainless steel dog bowl, dishwasher safe",
                Category: "Dogs",
                Dimensions: ProductDimensions.Create(8.5m, 8.5m, 3.0m, 1.2m),
                CreatedAt: DateTimeOffset.UtcNow));
            await session.SaveChangesAsync();
        }

        // Act
        var product = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url("/api/products/DOG-BOWL-001");
            s.StatusCodeShouldBeOk();
        });

        // Assert
        var result = product.ReadAsJson<ProductCatalogView>();
        result.ShouldNotBeNull();
        result!.Dimensions.ShouldNotBeNull();
        result.Dimensions!.Length.ShouldBeGreaterThan(0);
        result.Dimensions.Weight.ShouldBeGreaterThan(0);
    }
}
