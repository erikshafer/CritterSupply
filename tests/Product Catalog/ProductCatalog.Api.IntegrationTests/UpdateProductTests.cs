using Alba;
using ProductCatalog.Api.Products;
using ProductCatalog.Products;
using Shouldly;

namespace ProductCatalog.IntegrationTests;

public sealed class UpdateProductTests : IClassFixture<ProductCatalogFixture>
{
    private readonly ProductCatalogFixture _fixture;

    public UpdateProductTests(ProductCatalogFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CanUpdateProductName()
    {
        // Arrange
        var command = new UpdateProduct(
            "DOG-BOWL-001",
            Name: "Updated Premium Dog Bowl");

        // Act
        await _fixture.Host.Scenario(s =>
        {
            s.Put.Json(command).ToUrl("/api/products/DOG-BOWL-001");
            s.StatusCodeShouldBeOk();
        });

        // Assert - Verify the change
        var product = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url("/api/products/DOG-BOWL-001");
            s.StatusCodeShouldBeOk();
        });

        var result = product.ReadAsJson<Product>();
        result.Name.Value.ShouldBe("Updated Premium Dog Bowl");
        result.UpdatedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task CanUpdateProductDescription()
    {
        // Arrange
        var command = new UpdateProduct(
            "DOG-TOY-ROPE",
            Description: "New and improved description");

        // Act
        await _fixture.Host.Scenario(s =>
        {
            s.Put.Json(command).ToUrl("/api/products/DOG-TOY-ROPE");
            s.StatusCodeShouldBeOk();
        });

        // Assert
        var product = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url("/api/products/DOG-TOY-ROPE");
        });

        var result = product.ReadAsJson<Product>();
        result.Description.ShouldBe("New and improved description");
    }

    [Fact]
    public async Task CanUpdateProductCategory()
    {
        // Arrange
        var command = new UpdateProduct(
            "DOG-BOWL-001",
            Category: "Cats");

        // Act
        await _fixture.Host.Scenario(s =>
        {
            s.Put.Json(command).ToUrl("/api/products/DOG-BOWL-001");
            s.StatusCodeShouldBeOk();
        });

        // Assert
        var product = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url("/api/products/DOG-BOWL-001");
        });

        var result = product.ReadAsJson<Product>();
        result.Category.Value.ShouldBe("Cats");
    }

    [Fact]
    public async Task UpdateProduct_Returns404ForNonExistentSku()
    {
        // Arrange
        var command = new UpdateProduct(
            "NONEXISTENT-SKU",
            Name: "Updated Name");

        // Act & Assert
        await _fixture.Host.Scenario(s =>
        {
            s.Put.Json(command).ToUrl("/api/products/NONEXISTENT-SKU");
            s.StatusCodeShouldBe(404);
        });
    }

    [Fact]
    public async Task UpdateProduct_RejectsInvalidName()
    {
        // Arrange - @ symbol not allowed
        var command = new UpdateProduct(
            "DOG-BOWL-001",
            Name: "Invalid @ Name");

        // Act & Assert
        await _fixture.Host.Scenario(s =>
        {
            s.Put.Json(command).ToUrl("/api/products/DOG-BOWL-001");
            s.StatusCodeShouldBe(400);
        });
    }
}
