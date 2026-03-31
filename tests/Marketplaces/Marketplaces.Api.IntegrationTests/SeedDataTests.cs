using Marten;
using Marketplaces.CategoryMappings;
using Marketplaces.Marketplaces;

namespace Marketplaces.Api.IntegrationTests;

/// <summary>
/// Seed data verification tests — isolated from CRUD test classes that call
/// <see cref="TestFixture.CleanAllDocumentsAsync"/> in DisposeAsync.
/// This class re-seeds data in InitializeAsync so it is resilient to execution
/// order across test classes sharing the same <see cref="TestFixture"/> instance.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class SeedDataTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public SeedDataTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        // Re-seed data to ensure it exists regardless of execution order.
        // Other test classes may have cleaned documents in their DisposeAsync.
        await _fixture.ReseedAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // -------------------------------------------------------------------------
    // Marketplace seed data
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SeedData_ThreeMarketplaces_ExistOnStartup()
    {
        await using var session = _fixture.GetDocumentSession();

        var amazon = await session.LoadAsync<Marketplace>("AMAZON_US");
        var walmart = await session.LoadAsync<Marketplace>("WALMART_US");
        var ebay = await session.LoadAsync<Marketplace>("EBAY_US");

        amazon.ShouldNotBeNull();
        amazon!.DisplayName.ShouldBe("Amazon US");
        amazon.IsActive.ShouldBeTrue();
        amazon.IsOwnWebsite.ShouldBeFalse();

        walmart.ShouldNotBeNull();
        walmart!.DisplayName.ShouldBe("Walmart US");
        walmart.IsActive.ShouldBeTrue();

        ebay.ShouldNotBeNull();
        ebay!.DisplayName.ShouldBe("eBay US");
        ebay.IsActive.ShouldBeTrue();
    }

    // -------------------------------------------------------------------------
    // Category mapping seed data
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SeedData_EighteenCategoryMappings_ExistOnStartup()
    {
        await using var session = _fixture.GetDocumentSession();

        var allMappings = await session.Query<CategoryMapping>().ToListAsync();

        // 6 categories × 3 channels = 18 mappings
        allMappings.Count.ShouldBeGreaterThanOrEqualTo(18);

        // Verify a sample mapping from each channel
        var amazonDogs = await session.LoadAsync<CategoryMapping>("AMAZON_US:Dogs");
        amazonDogs.ShouldNotBeNull();
        amazonDogs!.MarketplaceCategoryId.ShouldBe("AMZN-PET-DOGS-001");

        var walmartCats = await session.LoadAsync<CategoryMapping>("WALMART_US:Cats");
        walmartCats.ShouldNotBeNull();
        walmartCats!.MarketplaceCategoryId.ShouldBe("WMT-PET-CATS-001");

        var ebayBirds = await session.LoadAsync<CategoryMapping>("EBAY_US:Birds");
        ebayBirds.ShouldNotBeNull();
        ebayBirds!.MarketplaceCategoryId.ShouldBe("EBAY-PET-BIRDS-001");
    }
}
