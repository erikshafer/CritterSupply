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
        // Arrange - Seed via event sourcing and also store document (UpdateProduct uses document store)
        using (var session = _fixture.GetDocumentSession())
        {
            var productId = Guid.NewGuid();
            session.Events.StartStream<CatalogProduct>(productId, new ProductCreated(
                ProductId: productId, Sku: "DOG-BOWL-001",
                Name: "Premium Stainless Steel Dog Bowl",
                Description: "Stainless steel dog bowl, dishwasher safe",
                Category: "Dogs", CreatedAt: DateTimeOffset.UtcNow));

            // Also store in document store since UpdateProduct handler reads from Product documents
            session.Store(Product.Create("DOG-BOWL-001",
                "Premium Stainless Steel Dog Bowl",
                "Stainless steel dog bowl, dishwasher safe",
                "Dogs"));
            await session.SaveChangesAsync();
        }

        var command = new UpdateProduct(
            "DOG-BOWL-001",
            Name: "Updated Premium Dog Bowl");

        // Act
        await _fixture.Host.Scenario(s =>
        {
            s.Put.Json(command).ToUrl("/api/products/DOG-BOWL-001");
            s.StatusCodeShouldBe(204);
        });

        // Assert - UpdateProduct writes to document store; verify via document query
        using var verifySession = _fixture.GetDocumentSession();
        var result = await verifySession.LoadAsync<Product>("DOG-BOWL-001");
        result.ShouldNotBeNull();
        result!.Name.Value.ShouldBe("Updated Premium Dog Bowl");
        result.UpdatedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task CanUpdateProductDescription()
    {
        // Arrange - Seed both document store and event stream
        using (var session = _fixture.GetDocumentSession())
        {
            var productId = Guid.NewGuid();
            session.Events.StartStream<CatalogProduct>(productId, new ProductCreated(
                ProductId: productId, Sku: "DOG-TOY-ROPE",
                Name: "Rope Tug Toy",
                Description: "Durable rope toy for interactive play",
                Category: "Dogs", CreatedAt: DateTimeOffset.UtcNow));

            session.Store(Product.Create("DOG-TOY-ROPE",
                "Rope Tug Toy",
                "Durable rope toy for interactive play",
                "Dogs"));
            await session.SaveChangesAsync();
        }

        var command = new UpdateProduct(
            "DOG-TOY-ROPE",
            Description: "New and improved description");

        // Act
        await _fixture.Host.Scenario(s =>
        {
            s.Put.Json(command).ToUrl("/api/products/DOG-TOY-ROPE");
            s.StatusCodeShouldBe(204);
        });

        // Assert - Verify via document store (UpdateProduct writes there)
        using var verifySession = _fixture.GetDocumentSession();
        var result = await verifySession.LoadAsync<Product>("DOG-TOY-ROPE");
        result.ShouldNotBeNull();
        result!.Description.ShouldBe("New and improved description");
    }

    [Fact]
    public async Task CanUpdateProductCategory()
    {
        // Arrange
        using (var session = _fixture.GetDocumentSession())
        {
            var productId = Guid.NewGuid();
            session.Events.StartStream<CatalogProduct>(productId, new ProductCreated(
                ProductId: productId, Sku: "DOG-BOWL-001",
                Name: "Premium Stainless Steel Dog Bowl",
                Description: "Stainless steel dog bowl, dishwasher safe",
                Category: "Dogs", CreatedAt: DateTimeOffset.UtcNow));

            session.Store(Product.Create("DOG-BOWL-001",
                "Premium Stainless Steel Dog Bowl",
                "Stainless steel dog bowl, dishwasher safe",
                "Dogs"));
            await session.SaveChangesAsync();
        }

        var command = new UpdateProduct(
            "DOG-BOWL-001",
            Category: "Cats");

        // Act
        await _fixture.Host.Scenario(s =>
        {
            s.Put.Json(command).ToUrl("/api/products/DOG-BOWL-001");
            s.StatusCodeShouldBe(204);
        });

        // Assert
        using var verifySession = _fixture.GetDocumentSession();
        var result = await verifySession.LoadAsync<Product>("DOG-BOWL-001");
        result.ShouldNotBeNull();
        result!.Category.ShouldBe("Cats");
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
        // Arrange
        using (var session = _fixture.GetDocumentSession())
        {
            var productId = Guid.NewGuid();
            session.Events.StartStream<CatalogProduct>(productId, new ProductCreated(
                ProductId: productId, Sku: "DOG-BOWL-001",
                Name: "Premium Stainless Steel Dog Bowl",
                Description: "Stainless steel dog bowl, dishwasher safe",
                Category: "Dogs", CreatedAt: DateTimeOffset.UtcNow));

            session.Store(Product.Create("DOG-BOWL-001",
                "Premium Stainless Steel Dog Bowl",
                "Stainless steel dog bowl, dishwasher safe",
                "Dogs"));
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
