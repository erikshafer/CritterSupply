using Marten;
using Marketplaces.Marketplaces;

/// <summary>
/// Seeds the three canonical marketplace documents on startup in Development.
/// Idempotent — skips seeding if any marketplace documents already exist.
/// OWN_WEBSITE is intentionally excluded (PO decision): it is the Listings BC fast-path channel,
/// not an external marketplace.
/// </summary>
public static class MarketplacesSeedData
{
    public static async Task SeedAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        await using var session = scope.ServiceProvider
            .GetRequiredService<IDocumentStore>()
            .LightweightSession();

        // Idempotency guard — skip if any marketplace documents already exist
        if (await session.Query<Marketplace>().AnyAsync())
            return;

        var now = DateTimeOffset.UtcNow;

        var marketplaces = new[]
        {
            new Marketplace
            {
                Id = "AMAZON_US",
                DisplayName = "Amazon US",
                IsActive = true,
                IsOwnWebsite = false,
                ApiCredentialVaultPath = "marketplace/amazon-us",
                CreatedAt = now,
                UpdatedAt = now
            },
            new Marketplace
            {
                Id = "WALMART_US",
                DisplayName = "Walmart US",
                IsActive = true,
                IsOwnWebsite = false,
                ApiCredentialVaultPath = "marketplace/walmart-us",
                CreatedAt = now,
                UpdatedAt = now
            },
            new Marketplace
            {
                Id = "EBAY_US",
                DisplayName = "eBay US",
                IsActive = true,
                IsOwnWebsite = false,
                ApiCredentialVaultPath = "marketplace/ebay-us",
                CreatedAt = now,
                UpdatedAt = now
            }
        };

        session.StoreObjects(marketplaces);
        await session.SaveChangesAsync();
    }
}
