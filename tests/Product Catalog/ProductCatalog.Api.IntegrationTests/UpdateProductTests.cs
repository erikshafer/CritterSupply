using ProductCatalog.Api.Products;
using ProductCatalog.Products;
using Shouldly;

namespace ProductCatalog.Api.IntegrationTests;

/// <summary>
/// Integration tests for the granular event-sourced product update handlers:
/// ChangeProductDescription, ChangeProductCategory, UpdateProductImages,
/// ChangeProductDimensions, UpdateProductTags.
/// Each handler appends an event and the inline projection updates ProductCatalogView.
/// </summary>
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

    #region ChangeProductDescription

    [Fact]
    public async Task CanChangeProductDescription()
    {
        // Arrange - Seed via event sourcing
        using (var session = _fixture.GetDocumentSession())
        {
            var productId = Guid.NewGuid();
            session.Events.StartStream<CatalogProduct>(productId, new ProductCreated(
                ProductId: productId, Sku: "DOG-TOY-ROPE",
                Name: "Rope Tug Toy",
                Description: "Durable rope toy for interactive play",
                Category: "Dogs", CreatedAt: DateTimeOffset.UtcNow));
            await session.SaveChangesAsync();
        }

        var command = new ChangeProductDescription("DOG-TOY-ROPE", "New and improved description");

        // Act
        await _fixture.Host.Scenario(s =>
        {
            s.Put.Json(command).ToUrl("/api/products/DOG-TOY-ROPE/description");
            s.StatusCodeShouldBe(204);
        });

        // Assert - Verify via ProductCatalogView projection
        var product = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url("/api/products/DOG-TOY-ROPE");
            s.StatusCodeShouldBeOk();
        });

        var result = product.ReadAsJson<ProductCatalogView>();
        result.ShouldNotBeNull();
        result!.Description.ShouldBe("New and improved description");
        result.UpdatedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task ChangeDescription_Returns404ForNonExistentSku()
    {
        var command = new ChangeProductDescription("NONEXISTENT-SKU", "Some description");

        await _fixture.Host.Scenario(s =>
        {
            s.Put.Json(command).ToUrl("/api/products/NONEXISTENT-SKU/description");
            s.StatusCodeShouldBe(404);
        });
    }

    #endregion

    #region ChangeProductCategory

    [Fact]
    public async Task CanChangeProductCategory()
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
            await session.SaveChangesAsync();
        }

        var command = new ChangeProductCategory("DOG-BOWL-001", "Cats");

        // Act
        await _fixture.Host.Scenario(s =>
        {
            s.Put.Json(command).ToUrl("/api/products/DOG-BOWL-001/category");
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
        result!.Category.ShouldBe("Cats");
        result.UpdatedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task ChangeCategory_Returns404ForNonExistentSku()
    {
        var command = new ChangeProductCategory("NONEXISTENT-SKU", "Cats");

        await _fixture.Host.Scenario(s =>
        {
            s.Put.Json(command).ToUrl("/api/products/NONEXISTENT-SKU/category");
            s.StatusCodeShouldBe(404);
        });
    }

    #endregion

    #region UpdateProductImages

    [Fact]
    public async Task CanUpdateProductImages()
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
            await session.SaveChangesAsync();
        }

        var command = new UpdateProductImages("DOG-BOWL-001",
        [
            new ProductImageDto("https://example.com/bowl-front.jpg", "Front view of dog bowl", 0),
            new ProductImageDto("https://example.com/bowl-side.jpg", "Side view of dog bowl", 1)
        ]);

        // Act
        await _fixture.Host.Scenario(s =>
        {
            s.Put.Json(command).ToUrl("/api/products/DOG-BOWL-001/images");
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
        result!.Images.Count.ShouldBe(2);
        result.Images[0].Url.ShouldBe("https://example.com/bowl-front.jpg");
        result.Images[1].Url.ShouldBe("https://example.com/bowl-side.jpg");
        result.UpdatedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task UpdateImages_Returns404ForNonExistentSku()
    {
        var command = new UpdateProductImages("NONEXISTENT-SKU",
        [
            new ProductImageDto("https://example.com/img.jpg", "Alt text", 0)
        ]);

        await _fixture.Host.Scenario(s =>
        {
            s.Put.Json(command).ToUrl("/api/products/NONEXISTENT-SKU/images");
            s.StatusCodeShouldBe(404);
        });
    }

    #endregion

    #region ChangeProductDimensions

    [Fact]
    public async Task CanChangeProductDimensions()
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
            await session.SaveChangesAsync();
        }

        var command = new ChangeProductDimensions("DOG-BOWL-001",
            new ProductDimensionsDto(10.5m, 10.5m, 4.0m, 1.5m));

        // Act
        await _fixture.Host.Scenario(s =>
        {
            s.Put.Json(command).ToUrl("/api/products/DOG-BOWL-001/dimensions");
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
        result!.Dimensions.ShouldNotBeNull();
        result.Dimensions!.Length.ShouldBe(10.5m);
        result.Dimensions.Width.ShouldBe(10.5m);
        result.Dimensions.Height.ShouldBe(4.0m);
        result.Dimensions.Weight.ShouldBe(1.5m);
        result.UpdatedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task ChangeDimensions_Returns404ForNonExistentSku()
    {
        var command = new ChangeProductDimensions("NONEXISTENT-SKU",
            new ProductDimensionsDto(1m, 1m, 1m, 1m));

        await _fixture.Host.Scenario(s =>
        {
            s.Put.Json(command).ToUrl("/api/products/NONEXISTENT-SKU/dimensions");
            s.StatusCodeShouldBe(404);
        });
    }

    #endregion

    #region UpdateProductTags

    [Fact]
    public async Task CanUpdateProductTags()
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
            await session.SaveChangesAsync();
        }

        var command = new UpdateProductTags("DOG-BOWL-001",
            ["stainless-steel", "dishwasher-safe", "large-breed"]);

        // Act
        await _fixture.Host.Scenario(s =>
        {
            s.Put.Json(command).ToUrl("/api/products/DOG-BOWL-001/tags");
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
        result!.Tags.Count.ShouldBe(3);
        result.Tags.ShouldContain("stainless-steel");
        result.Tags.ShouldContain("dishwasher-safe");
        result.Tags.ShouldContain("large-breed");
        result.UpdatedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task UpdateTags_Returns404ForNonExistentSku()
    {
        var command = new UpdateProductTags("NONEXISTENT-SKU", ["tag1"]);

        await _fixture.Host.Scenario(s =>
        {
            s.Put.Json(command).ToUrl("/api/products/NONEXISTENT-SKU/tags");
            s.StatusCodeShouldBe(404);
        });
    }

    #endregion

    #region Validation

    [Fact]
    public async Task ChangeDescription_RejectsEmptyDescription()
    {
        var command = new ChangeProductDescription("DOG-BOWL-001", "");

        await _fixture.Host.Scenario(s =>
        {
            s.Put.Json(command).ToUrl("/api/products/DOG-BOWL-001/description");
            s.StatusCodeShouldBe(400);
        });
    }

    [Fact]
    public async Task ChangeProductName_RejectsInvalidCharacters()
    {
        // Arrange - Seed via event sourcing
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

        // @ symbol not allowed in product names
        var command = new ChangeProductName("DOG-BOWL-001", "Invalid @ Name");

        await _fixture.Host.Scenario(s =>
        {
            s.Put.Json(command).ToUrl("/api/products/DOG-BOWL-001/name");
            s.StatusCodeShouldBe(400);
        });
    }

    #endregion
}

