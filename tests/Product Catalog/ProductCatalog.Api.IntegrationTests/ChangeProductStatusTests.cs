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

        var command = new ChangeProductStatus("DOG-BOWL-001", ProductStatus.Discontinued);

        // Act
        await _fixture.Host.Scenario(s =>
        {
            s.Patch.Json(command).ToUrl("/api/products/DOG-BOWL-001/status");
            s.StatusCodeShouldBe(204); // No Content for successful PATCH
        });

        // Assert
        var product = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url("/api/products/DOG-BOWL-001");
            s.StatusCodeShouldBeOk();
        });

        var result = product.ReadAsJson<Product>();
        result.Status.ShouldBe(ProductStatus.Discontinued);
        result.IsTerminal.ShouldBeTrue();
    }

    [Fact]
    public async Task CanChangeProductStatusToComingSoon()
    {
        // Arrange - Seed test product first
        var createProduct = Product.Create(
            "CAT-TREE-5FT",
            "5ft Cat Tree",
            "Multi-level cat tree with scratching posts",
            "Cats");

        using (var session = _fixture.GetDocumentSession())
        {
            session.Store(createProduct);
            await session.SaveChangesAsync();
        }

        var command = new ChangeProductStatus("CAT-TREE-5FT", ProductStatus.ComingSoon);

        // Act
        await _fixture.Host.Scenario(s =>
        {
            s.Patch.Json(command).ToUrl("/api/products/CAT-TREE-5FT/status");
            s.StatusCodeShouldBe(204); // No Content for successful PATCH
        });

        // Assert
        var product = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url("/api/products/CAT-TREE-5FT");
        });

        var result = product.ReadAsJson<Product>();
        result.Status.ShouldBe(ProductStatus.ComingSoon);
    }

    [Fact]
    public async Task CanChangeProductStatusToOutOfSeason()
    {
        // Arrange - Seed test product first
        var createProduct = Product.Create(
            "XMAS-PET-SWEATER",
            "Holiday Pet Sweater",
            "Festive sweater for small to medium pets",
            "Seasonal");

        using (var session = _fixture.GetDocumentSession())
        {
            session.Store(createProduct);
            await session.SaveChangesAsync();
        }

        var command = new ChangeProductStatus("XMAS-PET-SWEATER", ProductStatus.OutOfSeason);

        // Act
        await _fixture.Host.Scenario(s =>
        {
            s.Patch.Json(command).ToUrl("/api/products/XMAS-PET-SWEATER/status");
            s.StatusCodeShouldBe(204); // No Content for successful PATCH
        });

        // Assert
        var product = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url("/api/products/XMAS-PET-SWEATER");
        });

        var result = product.ReadAsJson<Product>();
        result.Status.ShouldBe(ProductStatus.OutOfSeason);
    }

    [Fact]
    public async Task ChangeStatus_Returns404ForNonExistentSku()
    {
        // Arrange
        var command = new ChangeProductStatus("NONEXISTENT-SKU", ProductStatus.Discontinued);

        // Act & Assert
        await _fixture.Host.Scenario(s =>
        {
            s.Patch.Json(command).ToUrl("/api/products/NONEXISTENT-SKU/status");
            s.StatusCodeShouldBe(404);
        });
    }
}
