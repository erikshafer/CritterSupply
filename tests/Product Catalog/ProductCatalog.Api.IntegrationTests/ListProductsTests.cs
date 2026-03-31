using ProductCatalog.Products;
using Shouldly;

namespace ProductCatalog.Api.IntegrationTests;

/// <summary>
/// Test-only DTO for deserializing the list products response.
/// Mirrors the anonymous type shape returned by ListProductsHandler.
/// Must be kept in sync if the handler response shape changes.
/// </summary>
public sealed record ProductListResponse(
    IReadOnlyList<ProductCatalogView> Items,
    int TotalCount,
    int Page,
    int PageSize);

[Collection(IntegrationTestCollection.Name)]
public sealed class ListProductsTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public ListProductsTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await _fixture.CleanAllDocumentsAsync();

        // Seed multiple test products via event sourcing so ProductCatalogView is populated
        var products = new[]
        {
            ("DOG-BOWL-001", "Premium Stainless Steel Dog Bowl", "Durable dog bowl", "Dogs"),
            ("DOG-TOY-ROPE", "Rope Tug Toy", "Interactive rope toy", "Dogs"),
            ("CAT-TREE-5FT", "5ft Cat Tree", "Multi-level cat tree", "Cats"),
            ("CAT-TOY-MOUSE", "Toy Mouse", "Catnip mouse toy", "Cats"),
            ("BIRD-CAGE-LG", "Large Bird Cage", "Spacious bird habitat", "Birds"),
            ("FISH-TANK-10G", "10 Gallon Fish Tank", "Glass aquarium", "Fish")
        };

        using var session = _fixture.GetDocumentSession();
        foreach (var (sku, name, description, category) in products)
        {
            var productId = Guid.NewGuid();
            session.Events.StartStream<CatalogProduct>(productId, new ProductCreated(
                ProductId: productId, Sku: sku, Name: name,
                Description: description, Category: category,
                CreatedAt: DateTimeOffset.UtcNow));
        }
        await session.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task CanListAllProducts()
    {
        // Act
        var response = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url("/api/products");
            s.StatusCodeShouldBeOk();
        });

        // Assert
        var result = response.ReadAsJson<ProductListResponse>();
        result.ShouldNotBeNull();
        result!.Items.ShouldNotBeEmpty();
        result.TotalCount.ShouldBeGreaterThanOrEqualTo(6);
        result.Page.ShouldBe(1);
        result.PageSize.ShouldBe(20);
    }

    [Fact]
    public async Task CanListProductsWithPagination()
    {
        // Act - Get page 1 with 3 products per page
        var response = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url("/api/products?page=1&pageSize=3");
            s.StatusCodeShouldBeOk();
        });

        // Assert
        var result = response.ReadAsJson<ProductListResponse>();
        result.ShouldNotBeNull();
        result!.Page.ShouldBe(1);
        result.PageSize.ShouldBe(3);
        result.Items.Count.ShouldBeLessThanOrEqualTo(3);
        result.TotalCount.ShouldBeGreaterThanOrEqualTo(6);
    }

    [Fact]
    public async Task CanFilterProductsByCategory()
    {
        // Act
        var response = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url("/api/products?category=Dogs");
            s.StatusCodeShouldBeOk();
        });

        // Assert
        var result = response.ReadAsJson<ProductListResponse>();
        result.ShouldNotBeNull();
        result!.Items.ShouldNotBeEmpty();
        result.Items.ShouldAllBe(p => p.Category == "Dogs");
    }

    [Fact]
    public async Task CanFilterProductsByStatus()
    {
        // Act
        var response = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url("/api/products?status=Active");
            s.StatusCodeShouldBeOk();
        });

        // Assert
        var result = response.ReadAsJson<ProductListResponse>();
        result.ShouldNotBeNull();
        result!.Items.ShouldNotBeEmpty();
        result.Items.ShouldAllBe(p => p.Status == ProductStatus.Active);
    }

    [Fact]
    public async Task CanFilterProductsByCategoryAndStatus()
    {
        // Act
        var response = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url("/api/products?category=Cats&status=Active");
            s.StatusCodeShouldBeOk();
        });

        // Assert
        var result = response.ReadAsJson<ProductListResponse>();
        result.ShouldNotBeNull();
        result!.Items.ShouldNotBeEmpty();
        result.Items.ShouldAllBe(p => p.Category == "Cats" && p.Status == ProductStatus.Active);
    }

    [Fact]
    public async Task ListProducts_ReturnsEmptyListForNonExistentCategory()
    {
        // Act
        var response = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url("/api/products?category=NonExistentCategory");
            s.StatusCodeShouldBeOk();
        });

        // Assert
        var result = response.ReadAsJson<ProductListResponse>();
        result.ShouldNotBeNull();
        result!.Items.ShouldBeEmpty();
        result.TotalCount.ShouldBe(0);
    }
}
