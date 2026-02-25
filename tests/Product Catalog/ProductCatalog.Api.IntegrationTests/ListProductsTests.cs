using ProductCatalog.Api.Products;
using ProductCatalog.Products;
using Shouldly;

namespace ProductCatalog.Api.IntegrationTests;

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

        // Seed multiple test products for list/filter scenarios
        var products = new[]
        {
            Product.Create("DOG-BOWL-001", "Premium Stainless Steel Dog Bowl", "Durable dog bowl", "Dogs"),
            Product.Create("DOG-TOY-ROPE", "Rope Tug Toy", "Interactive rope toy", "Dogs"),
            Product.Create("CAT-TREE-5FT", "5ft Cat Tree", "Multi-level cat tree", "Cats"),
            Product.Create("CAT-TOY-MOUSE", "Toy Mouse", "Catnip mouse toy", "Cats"),
            Product.Create("BIRD-CAGE-LG", "Large Bird Cage", "Spacious bird habitat", "Birds"),
            Product.Create("FISH-TANK-10G", "10 Gallon Fish Tank", "Glass aquarium", "Fish")
        };

        using var session = _fixture.GetDocumentSession();
        foreach (var product in products)
        {
            session.Store(product);
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
        var result = response.ReadAsJson<ProductListResult>();
        result.ShouldNotBeNull();
        result.Products.ShouldNotBeEmpty();
        result.TotalCount.ShouldBeGreaterThanOrEqualTo(6); // At least the 6 we seeded
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
        var result = response.ReadAsJson<ProductListResult>();
        result.Page.ShouldBe(1);
        result.PageSize.ShouldBe(3);
        result.Products.Count.ShouldBeLessThanOrEqualTo(3);
        result.TotalCount.ShouldBeGreaterThanOrEqualTo(6); // Total products seeded
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
        var result = response.ReadAsJson<ProductListResult>();
        result.Products.ShouldNotBeEmpty();
        result.Products.ShouldAllBe(p => p.Category == "Dogs");
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
        var result = response.ReadAsJson<ProductListResult>();
        result.Products.ShouldNotBeEmpty();
        result.Products.ShouldAllBe(p => p.Status == ProductStatus.Active);
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
        var result = response.ReadAsJson<ProductListResult>();
        result.Products.ShouldNotBeEmpty();
        result.Products.ShouldAllBe(p => p.Category == "Cats" && p.Status == ProductStatus.Active);
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
        var result = response.ReadAsJson<ProductListResult>();
        result.Products.ShouldBeEmpty();
        result.TotalCount.ShouldBe(0);
    }
}
