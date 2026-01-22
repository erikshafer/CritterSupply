using Alba;
using ProductCatalog.Api.Products;
using ProductCatalog.Products;
using Shouldly;

namespace ProductCatalog.IntegrationTests;

public sealed class ChangeProductStatusTests : IClassFixture<ProductCatalogFixture>
{
    private readonly ProductCatalogFixture _fixture;

    public ChangeProductStatusTests(ProductCatalogFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CanChangeProductStatusToDiscontinued()
    {
        // Arrange
        var command = new ChangeProductStatus("DOG-BOWL-001", ProductStatus.Discontinued);

        // Act
        await _fixture.Host.Scenario(s =>
        {
            s.Patch.Json(command).ToUrl("/api/products/DOG-BOWL-001/status");
            s.StatusCodeShouldBeOk();
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
        // Arrange
        var command = new ChangeProductStatus("CAT-TREE-5FT", ProductStatus.ComingSoon);

        // Act
        await _fixture.Host.Scenario(s =>
        {
            s.Patch.Json(command).ToUrl("/api/products/CAT-TREE-5FT/status");
            s.StatusCodeShouldBeOk();
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
        // Arrange
        var command = new ChangeProductStatus("XMAS-PET-SWEATER", ProductStatus.OutOfSeason);

        // Act
        await _fixture.Host.Scenario(s =>
        {
            s.Patch.Json(command).ToUrl("/api/products/XMAS-PET-SWEATER/status");
            s.StatusCodeShouldBeOk();
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
