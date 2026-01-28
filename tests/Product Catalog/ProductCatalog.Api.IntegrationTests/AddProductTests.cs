using Alba;
using ProductCatalog.Api.Products;
using ProductCatalog.Products;
using Shouldly;

namespace ProductCatalog.IntegrationTests;

[Collection(IntegrationTestCollection.Name)]
public sealed class AddProductTests : IClassFixture<ProductCatalogFixture>
{
    private readonly ProductCatalogFixture _fixture;

    public AddProductTests(ProductCatalogFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CanAddNewProduct()
    {
        // Arrange
        var command = new AddProduct(
            "TEST-SKU-001",
            "Test Product Name",
            "This is a test product description",
            "Dogs",
            null,
            null,
            "TestBrand",
            new List<ProductImageDto>
            {
                new("https://via.placeholder.com/400", "Test image", 0)
            },
            new List<string> { "test", "sample" },
            new ProductDimensionsDto(10, 8, 6, 2));

        // Act
        var result = await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(command).ToUrl("/api/products");
            s.StatusCodeShouldBe(201);
            s.Header("Location").SingleValueShouldMatch(@"/api/products/TEST-SKU-001");
        });

        // Assert
        result.Context.Response.StatusCode.ShouldBe(201);
    }

    [Fact]
    public async Task AddProduct_RejectsInvalidSku()
    {
        // Arrange - lowercase SKU (violates uppercase constraint)
        var command = new AddProduct(
            "lowercase-sku",
            "Test Product",
            "Description",
            "Dogs");

        // Act & Assert
        await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(command).ToUrl("/api/products");
            s.StatusCodeShouldBe(400);
        });
    }

    [Fact]
    public async Task AddProduct_RejectsEmptyName()
    {
        // Arrange
        var command = new AddProduct(
            "VALID-SKU",
            "",
            "Description",
            "Dogs");

        // Act & Assert
        await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(command).ToUrl("/api/products");
            s.StatusCodeShouldBe(400);
        });
    }

    [Fact]
    public async Task AddProduct_RejectsNameTooLong()
    {
        // Arrange - 101 characters (exceeds 100 char limit)
        var command = new AddProduct(
            "VALID-SKU",
            new string('A', 101),
            "Description",
            "Dogs");

        // Act & Assert
        await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(command).ToUrl("/api/products");
            s.StatusCodeShouldBe(400);
        });
    }

    [Fact]
    public async Task AddProduct_RejectsInvalidCharactersInName()
    {
        // Arrange - @ symbol not allowed
        var command = new AddProduct(
            "VALID-SKU",
            "Product @ Discount",
            "Description",
            "Dogs");

        // Act & Assert
        await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(command).ToUrl("/api/products");
            s.StatusCodeShouldBe(400);
        });
    }
}
