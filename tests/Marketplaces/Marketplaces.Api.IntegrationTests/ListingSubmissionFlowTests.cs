using Marketplaces.CategoryMappings;
using Marketplaces.Marketplaces;
using Marketplaces.Products;
using Messages.Contracts.Listings;
using Messages.Contracts.Marketplaces;

namespace Marketplaces.Api.IntegrationTests;

/// <summary>
/// Integration tests for the ListingApproved consumer handler.
/// Verifies adapter invocation, OWN_WEBSITE guard, category mapping validation,
/// ProductSummaryView ACL lookups, and integration message publishing.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class ListingSubmissionFlowTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public ListingSubmissionFlowTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await _fixture.CleanAllDocumentsAsync();
    }

    // -------------------------------------------------------------------------
    // Happy path — Amazon
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ListingApproved_AmazonChannel_CallsAdapterAndPublishesActivated()
    {
        // Arrange: ensure marketplace, category mapping, and product summary exist
        await SeedMarketplaceAndMappingAsync("AMAZON_US", "Dogs", "AMZN-PET-DOGS-001");
        await SeedProductSummaryAsync("DOG-BOWL-001", "Premium Dog Bowl", "Dogs", 29.99m);

        var message = new ListingApproved(
            ListingId: Guid.NewGuid(),
            Sku: "DOG-BOWL-001",
            ChannelCode: "AMAZON_US",
            ProductName: "Premium Dog Bowl",
            Category: "Dogs",
            Price: 29.99m,
            OccurredAt: DateTimeOffset.UtcNow);

        // Act
        var tracked = await _fixture.ExecuteAndWaitAsync(message);

        // Assert: MarketplaceListingActivated was published
        var activated = tracked.Sent.MessagesOf<MarketplaceListingActivated>().ToList();
        activated.Count.ShouldBe(1);
        activated[0].ListingId.ShouldBe(message.ListingId);
        activated[0].Sku.ShouldBe("DOG-BOWL-001");
        activated[0].ChannelCode.ShouldBe("AMAZON_US");
        activated[0].ExternalListingId.ShouldStartWith("amzn-");
    }

    // -------------------------------------------------------------------------
    // Happy path — Walmart (M38.0: schedules CheckWalmartFeedStatus poll instead of immediate activation)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ListingApproved_WalmartChannel_SchedulesPollInsteadOfPublishingActivated()
    {
        await SeedMarketplaceAndMappingAsync("WALMART_US", "Cats", "WMT-PET-CATS-001");
        await SeedProductSummaryAsync("CAT-TOY-001", "Interactive Cat Toy", "Cats", 14.99m);

        var message = new ListingApproved(
            ListingId: Guid.NewGuid(),
            Sku: "CAT-TOY-001",
            ChannelCode: "WALMART_US",
            ProductName: "Interactive Cat Toy",
            Category: "Cats",
            Price: 14.99m,
            OccurredAt: DateTimeOffset.UtcNow);

        var tracked = await _fixture.ExecuteAndWaitAsync(message);

        // Walmart: MarketplaceListingActivated should NOT be published immediately —
        // instead a CheckWalmartFeedStatus poll is scheduled.
        var activated = tracked.Sent.MessagesOf<MarketplaceListingActivated>().ToList();
        activated.ShouldBeEmpty();

        // No rejection either — the submission succeeded
        var rejected = tracked.Sent.MessagesOf<MarketplaceSubmissionRejected>().ToList();
        rejected.ShouldBeEmpty();
    }

    // -------------------------------------------------------------------------
    // OWN_WEBSITE guard — skip adapter
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ListingApproved_OwnWebsiteChannel_SkipsAdapter()
    {
        var message = new ListingApproved(
            ListingId: Guid.NewGuid(),
            Sku: "OWN-WEB-001",
            ChannelCode: "OWN_WEBSITE",
            ProductName: "Own Website Product",
            Category: "Dogs",
            Price: 19.99m,
            OccurredAt: DateTimeOffset.UtcNow);

        var tracked = await _fixture.ExecuteAndWaitAsync(message);

        // No messages should be published — OWN_WEBSITE is skipped
        var activated = tracked.Sent.MessagesOf<MarketplaceListingActivated>().ToList();
        activated.ShouldBeEmpty();

        var rejected = tracked.Sent.MessagesOf<MarketplaceSubmissionRejected>().ToList();
        rejected.ShouldBeEmpty();
    }

    // -------------------------------------------------------------------------
    // Missing category mapping — publishes rejection (GR-NEW-2)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ListingApproved_MissingCategoryMapping_PublishesRejected()
    {
        // Seed marketplace and product summary but NOT category mapping for "Exotic Pets"
        await SeedMarketplaceAsync("AMAZON_US");
        await SeedProductSummaryAsync("EXOTIC-001", "Exotic Pet Cage", "Exotic Pets", 89.99m);

        var message = new ListingApproved(
            ListingId: Guid.NewGuid(),
            Sku: "EXOTIC-001",
            ChannelCode: "AMAZON_US",
            ProductName: "Exotic Pet Cage",
            Category: "Exotic Pets",
            Price: 89.99m,
            OccurredAt: DateTimeOffset.UtcNow);

        var tracked = await _fixture.ExecuteAndWaitAsync(message);

        // MarketplaceSubmissionRejected should be published
        var rejected = tracked.Sent.MessagesOf<MarketplaceSubmissionRejected>().ToList();
        rejected.Count.ShouldBe(1);
        rejected[0].ListingId.ShouldBe(message.ListingId);
        rejected[0].Reason.ShouldContain("No category mapping configured");
        rejected[0].Reason.ShouldContain("AMAZON_US:Exotic Pets");

        // No activation should have been published
        var activated = tracked.Sent.MessagesOf<MarketplaceListingActivated>().ToList();
        activated.ShouldBeEmpty();
    }

    // -------------------------------------------------------------------------
    // Inactive marketplace — publishes rejection
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ListingApproved_InactiveMarketplace_PublishesRejected()
    {
        // Seed inactive marketplace + category mapping + product summary
        await SeedMarketplaceAndMappingAsync("EBAY_US", "Dogs", "EBAY-PET-DOGS-001", isActive: false);
        await SeedProductSummaryAsync("DOG-LEASH-001", "Premium Dog Leash", "Dogs", 34.99m);

        var message = new ListingApproved(
            ListingId: Guid.NewGuid(),
            Sku: "DOG-LEASH-001",
            ChannelCode: "EBAY_US",
            ProductName: "Premium Dog Leash",
            Category: "Dogs",
            Price: 34.99m,
            OccurredAt: DateTimeOffset.UtcNow);

        var tracked = await _fixture.ExecuteAndWaitAsync(message);

        var rejected = tracked.Sent.MessagesOf<MarketplaceSubmissionRejected>().ToList();
        rejected.Count.ShouldBe(1);
        rejected[0].Reason.ShouldContain("not active");

        var activated = tracked.Sent.MessagesOf<MarketplaceListingActivated>().ToList();
        activated.ShouldBeEmpty();
    }

    // -------------------------------------------------------------------------
    // Missing ProductSummaryView — publishes rejection
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ListingApproved_MissingProductSummaryView_PublishesRejected()
    {
        // Seed marketplace and category mapping but NOT product summary
        await SeedMarketplaceAndMappingAsync("AMAZON_US", "Dogs", "AMZN-PET-DOGS-001");

        var message = new ListingApproved(
            ListingId: Guid.NewGuid(),
            Sku: "UNKNOWN-SKU-001",
            ChannelCode: "AMAZON_US",
            ProductName: "Unknown Product",
            Category: "Dogs",
            Price: 19.99m,
            OccurredAt: DateTimeOffset.UtcNow);

        var tracked = await _fixture.ExecuteAndWaitAsync(message);

        var rejected = tracked.Sent.MessagesOf<MarketplaceSubmissionRejected>().ToList();
        rejected.Count.ShouldBe(1);
        rejected[0].ListingId.ShouldBe(message.ListingId);
        rejected[0].Sku.ShouldBe("UNKNOWN-SKU-001");
        rejected[0].Reason.ShouldContain("ProductSummaryView missing");

        var activated = tracked.Sent.MessagesOf<MarketplaceListingActivated>().ToList();
        activated.ShouldBeEmpty();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task SeedProductSummaryAsync(
        string sku, string productName, string category, decimal? basePrice = null)
    {
        await using var session = _fixture.GetDocumentSession();
        var existing = await session.LoadAsync<ProductSummaryView>(sku);
        if (existing is null)
        {
            session.Store(new ProductSummaryView
            {
                Id = sku,
                ProductName = productName,
                Category = category,
                BasePrice = basePrice,
                Status = ProductSummaryStatus.Active
            });
            await session.SaveChangesAsync();
        }
    }

    private async Task SeedMarketplaceAsync(string channelCode, bool isActive = true)
    {
        await using var session = _fixture.GetDocumentSession();
        var existing = await session.LoadAsync<Marketplace>(channelCode);
        if (existing is null)
        {
            var now = DateTimeOffset.UtcNow;
            session.Store(new Marketplace
            {
                Id = channelCode,
                DisplayName = $"{channelCode} Test",
                IsActive = isActive,
                IsOwnWebsite = false,
                ApiCredentialVaultPath = $"marketplace/{channelCode.ToLowerInvariant()}",
                CreatedAt = now,
                UpdatedAt = now
            });
            await session.SaveChangesAsync();
        }
        else if (existing.IsActive != isActive)
        {
            existing.IsActive = isActive;
            session.Store(existing);
            await session.SaveChangesAsync();
        }
    }

    private async Task SeedMarketplaceAndMappingAsync(
        string channelCode, string category, string marketplaceCategoryId, bool isActive = true)
    {
        await SeedMarketplaceAsync(channelCode, isActive);

        await using var session = _fixture.GetDocumentSession();
        var mappingId = $"{channelCode}:{category}";
        var existingMapping = await session.LoadAsync<CategoryMapping>(mappingId);
        if (existingMapping is null)
        {
            session.Store(new CategoryMapping
            {
                Id = mappingId,
                ChannelCode = channelCode,
                InternalCategory = category,
                MarketplaceCategoryId = marketplaceCategoryId,
                LastVerifiedAt = DateTimeOffset.UtcNow
            });
            await session.SaveChangesAsync();
        }
    }
}
