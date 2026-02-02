using Marten;
using ProductCatalog.Products;

namespace ProductCatalog.IntegrationTests;

/// <summary>
/// Seeds test data for Product Catalog integration tests.
/// </summary>
public static class SeedData
{
    public static async Task SeedProductsAsync(IDocumentStore store)
    {
        await using var session = store.LightweightSession();

        // Seed products for testing
        var products = new[]
        {
            // DOG-BOWL-001 - used by GetProductTests (needs name, images, dimensions)
            Product.Create(
                "DOG-BOWL-001",
                "Premium Stainless Steel Dog Bowl",
                "Stainless steel dog bowl, dishwasher safe",
                "Dogs",
                images: new[]
                {
                    ProductImage.Create("https://placeholder.com/dog-bowl-001.jpg", "Premium dog bowl image", 0)
                }.ToList().AsReadOnly(),
                dimensions: ProductDimensions.Create(8.5m, 8.5m, 3.0m, 1.2m)),

            // DOG-TOY-ROPE - used by UpdateProductTests
            Product.Create(
                "DOG-TOY-ROPE",
                "Rope Tug Toy",
                "Durable rope toy for interactive play",
                "Dogs"),

            // CAT-TREE-5FT - used by ChangeProductStatusTests and filtering
            Product.Create(
                "CAT-TREE-5FT",
                "5ft Cat Tree",
                "Multi-level cat tree with scratching posts",
                "Cats"),

            // XMAS-PET-SWEATER - used by ChangeProductStatusTests (OutOfSeason)
            Product.Create(
                "XMAS-PET-SWEATER",
                "Holiday Pet Sweater",
                "Festive sweater for small to medium pets",
                "Seasonal")
        };

        foreach (var product in products)
        {
            session.Store(product);
        }

        await session.SaveChangesAsync();
    }
}
