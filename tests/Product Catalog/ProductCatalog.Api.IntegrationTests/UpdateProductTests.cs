using ProductCatalog.Api.Products;
using ProductCatalog.Products;
using Shouldly;

namespace ProductCatalog.Api.IntegrationTests;

[Collection(IntegrationTestCollection.Name)]
public sealed class UpdateProductTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public UpdateProductTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.CleanAllDocumentsAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task CanUpdateProductName()
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

        var command = new UpdateProduct(
            "DOG-BOWL-001",
            Name: "Updated Premium Dog Bowl");

        // Act
        await _fixture.Host.Scenario(s =>
        {
            s.Put.Json(command).ToUrl("/api/products/DOG-BOWL-001");
            s.StatusCodeShouldBe(204); // No Content for successful PUT
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
        // Arrange - Seed test product first
        var createProduct = Product.Create(
            "DOG-TOY-ROPE",
            "Rope Tug Toy",
            "Durable rope toy for interactive play",
            "Dogs");

        using (var session = _fixture.GetDocumentSession())
        {
            session.Store(createProduct);
            await session.SaveChangesAsync();
        }

        var command = new UpdateProduct(
            "DOG-TOY-ROPE",
            Description: "New and improved description");

        // Act
        await _fixture.Host.Scenario(s =>
        {
            s.Put.Json(command).ToUrl("/api/products/DOG-TOY-ROPE");
            s.StatusCodeShouldBe(204); // No Content for successful PUT
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

        var command = new UpdateProduct(
            "DOG-BOWL-001",
            Category: "Cats");

        // Act
        await _fixture.Host.Scenario(s =>
        {
            s.Put.Json(command).ToUrl("/api/products/DOG-BOWL-001");
            s.StatusCodeShouldBe(204); // No Content for successful PUT
        });

        // Assert
        var product = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url("/api/products/DOG-BOWL-001");
        });

        var result = product.ReadAsJson<Product>();
        result.Category.ShouldBe("Cats");
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

        // @ symbol not allowed
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
