using Marten;
using Marketplaces.CategoryMappings;
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

        // Seed category mappings — 6 categories × 3 channels = 18 documents
        await SeedCategoryMappingsAsync(session);
    }

    private static async Task SeedCategoryMappingsAsync(IDocumentSession session)
    {
        if (await session.Query<CategoryMapping>().AnyAsync())
            return;

        var now = DateTimeOffset.UtcNow;

        var categories = new (string Internal, string AmazonId, string WalmartId, string EbayId)[]
        {
            ("Dogs", "AMZN-PET-DOGS-001", "WMT-PET-DOGS-001", "EBAY-PET-DOGS-001"),
            ("Cats", "AMZN-PET-CATS-001", "WMT-PET-CATS-001", "EBAY-PET-CATS-001"),
            ("Birds", "AMZN-PET-BIRDS-001", "WMT-PET-BIRDS-001", "EBAY-PET-BIRDS-001"),
            ("Reptiles", "AMZN-PET-REPT-001", "WMT-PET-REPT-001", "EBAY-PET-REPT-001"),
            ("Fish & Aquatics", "AMZN-PET-FISH-001", "WMT-PET-FISH-001", "EBAY-PET-FISH-001"),
            ("Small Animals", "AMZN-PET-SMALL-001", "WMT-PET-SMALL-001", "EBAY-PET-SMALL-001"),
        };

        var channels = new[] { "AMAZON_US", "WALMART_US", "EBAY_US" };

        foreach (var (internalCategory, amazonId, walmartId, ebayId) in categories)
        {
            var channelIds = new Dictionary<string, string>
            {
                ["AMAZON_US"] = amazonId,
                ["WALMART_US"] = walmartId,
                ["EBAY_US"] = ebayId,
            };

            foreach (var channel in channels)
            {
                session.Store(new CategoryMapping
                {
                    Id = $"{channel}:{internalCategory}",
                    ChannelCode = channel,
                    InternalCategory = internalCategory,
                    MarketplaceCategoryId = channelIds[channel],
                    LastVerifiedAt = now
                });
            }
        }

        await session.SaveChangesAsync();
    }
}
