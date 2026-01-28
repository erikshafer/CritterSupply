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
            Product.Create(
                "DOG-BOWL-001",
                "Premium Dog Bowl",
                "Stainless steel dog bowl, dishwasher safe",
                "Dogs"),

            Product.Create(
                "DOG-TOY-ROPE",
                "Rope Tug Toy",
                "Durable rope toy for interactive play",
                "Dogs"),

            Product.Create(
                "CAT-TREE-5FT",
                "5ft Cat Tree",
                "Multi-level cat tree with scratching posts",
                "Cats"),

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
