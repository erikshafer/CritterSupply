using ProductCatalog.Api.Products;
using ProductCatalog.Products;
using Shouldly;

namespace ProductCatalog.Api.IntegrationTests;

[Collection(IntegrationTestCollection.Name)]
public sealed class ChangeProductStatusTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public ChangeProductStatusTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.CleanAllDocumentsAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task CanChangeProductStatusToDiscontinued()
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

        var command = new ChangeProductStatusCommand("DOG-BOWL-001", ProductStatus.Discontinued);

        // Act
        await _fixture.Host.Scenario(s =>
        {
            s.Patch.Json(command).ToUrl("/api/products/DOG-BOWL-001/status");
            s.StatusCodeShouldBe(204);
        });

        // Assert
        var product = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url("/api/products/DOG-BOWL-001");
            s.StatusCodeShouldBeOk();
        });

        var result = product.ReadAsJson<ProductCatalogView>();
        result.ShouldNotBeNull();
        result!.Status.ShouldBe(ProductStatus.Discontinued);
    }

    [Fact]
    public async Task CanChangeProductStatusToComingSoon()
    {
        // Arrange - Seed via event sourcing
        using (var session = _fixture.GetDocumentSession())
        {
            var productId = Guid.NewGuid();
            session.Events.StartStream<CatalogProduct>(productId, new ProductCreated(
                ProductId: productId, Sku: "CAT-TREE-5FT",
                Name: "5ft Cat Tree",
                Description: "Multi-level cat tree with scratching posts",
                Category: "Cats", CreatedAt: DateTimeOffset.UtcNow));
            await session.SaveChangesAsync();
        }

        var command = new ChangeProductStatusCommand("CAT-TREE-5FT", ProductStatus.ComingSoon);

        // Act
        await _fixture.Host.Scenario(s =>
        {
            s.Patch.Json(command).ToUrl("/api/products/CAT-TREE-5FT/status");
            s.StatusCodeShouldBe(204);
        });

        // Assert
        var product = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url("/api/products/CAT-TREE-5FT");
        });

        var result = product.ReadAsJson<ProductCatalogView>();
        result.ShouldNotBeNull();
        result!.Status.ShouldBe(ProductStatus.ComingSoon);
    }

    [Fact]
    public async Task CanChangeProductStatusToOutOfSeason()
    {
        // Arrange - Seed via event sourcing
        using (var session = _fixture.GetDocumentSession())
        {
            var productId = Guid.NewGuid();
            session.Events.StartStream<CatalogProduct>(productId, new ProductCreated(
                ProductId: productId, Sku: "XMAS-PET-SWEATER",
                Name: "Holiday Pet Sweater",
                Description: "Festive sweater for small to medium pets",
                Category: "Seasonal", CreatedAt: DateTimeOffset.UtcNow));
            await session.SaveChangesAsync();
        }

        var command = new ChangeProductStatusCommand("XMAS-PET-SWEATER", ProductStatus.OutOfSeason);

        // Act
        await _fixture.Host.Scenario(s =>
        {
            s.Patch.Json(command).ToUrl("/api/products/XMAS-PET-SWEATER/status");
            s.StatusCodeShouldBe(204);
        });

        // Assert
        var product = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url("/api/products/XMAS-PET-SWEATER");
        });

        var result = product.ReadAsJson<ProductCatalogView>();
        result.ShouldNotBeNull();
        result!.Status.ShouldBe(ProductStatus.OutOfSeason);
    }

    [Fact]
    public async Task ChangeStatus_Returns404ForNonExistentSku()
    {
        // Arrange
        var command = new ChangeProductStatusCommand("NONEXISTENT-SKU", ProductStatus.Discontinued);

        // Act & Assert
        await _fixture.Host.Scenario(s =>
        {
            s.Patch.Json(command).ToUrl("/api/products/NONEXISTENT-SKU/status");
            s.StatusCodeShouldBe(404);
        });
    }
}
