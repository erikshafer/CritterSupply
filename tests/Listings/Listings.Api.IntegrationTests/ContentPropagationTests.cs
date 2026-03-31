using Listings.Listing;
using Listings.ProductSummary;
using Messages.Contracts.ProductCatalog;

namespace Listings.Api.IntegrationTests;

/// <summary>
/// Tests content propagation from ProductContentUpdated to Live listings only.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class ContentPropagationTests
{
    private readonly TestFixture _fixture;

    public ContentPropagationTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task SeedProductSummaryAsync(
        string sku,
        string name = "Test Product",
        ProductSummaryStatus status = ProductSummaryStatus.Active)
    {
        using var session = _fixture.GetDocumentSession();
        session.Store(new ProductSummaryView
        {
            Id = sku,
            Name = name,
            Description = "Original description",
            Category = "Dogs",
            Status = status,
            Brand = "TestBrand",
            HasDimensions = false,
            ImageUrls = []
        });
        await session.SaveChangesAsync();
    }

    [Fact]
    public async Task ContentPropagated_ToLiveListing_UpdatesListingContent()
    {
        // Arrange
        await _fixture.CleanAllDocumentsAsync();
        var sku = "CONTENT-LIVE-001";
        await SeedProductSummaryAsync(sku, "Original Name");

        // Create and activate listing (Draft → Live via OWN_WEBSITE)
        await _fixture.ExecuteAndWaitAsync(new CreateListing(sku, "OWN_WEBSITE", "Original content"));
        var listingId = ListingStreamId.Compute(sku, "OWN_WEBSITE");
        await _fixture.ExecuteAndWaitAsync(new ActivateListing(listingId));

        // Act: Propagate content update
        var contentUpdate = new ProductContentUpdated(
            sku,
            "Updated Product Name",
            "Updated product description",
            DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(contentUpdate);

        // Assert: Live listing content is updated
        using var session = _fixture.GetDocumentSession();
        var listing = await session.Events.AggregateStreamAsync<Listing.Listing>(listingId);
        listing.ShouldNotBeNull();
        listing.Status.ShouldBe(ListingStatus.Live);
        listing.ProductName.ShouldBe("Updated Product Name");
        listing.Content.ShouldBe("Updated product description");
    }

    [Fact]
    public async Task ContentPropagated_ToDraftListing_IsIgnored()
    {
        // Arrange
        await _fixture.CleanAllDocumentsAsync();
        var sku = "CONTENT-DRAFT-001";
        await SeedProductSummaryAsync(sku, "Draft Product");

        // Create listing (stays in Draft)
        await _fixture.ExecuteAndWaitAsync(new CreateListing(sku, "AMAZON_US", "Original content"));
        var listingId = ListingStreamId.Compute(sku, "AMAZON_US");

        // Act: Propagate content update
        var contentUpdate = new ProductContentUpdated(
            sku,
            "Updated Product Name",
            "Updated product description",
            DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(contentUpdate);

        // Assert: Draft listing content is NOT updated (still has original from ProductSummary)
        using var session = _fixture.GetDocumentSession();
        var listing = await session.Events.AggregateStreamAsync<Listing.Listing>(listingId);
        listing.ShouldNotBeNull();
        listing.Status.ShouldBe(ListingStatus.Draft);
        listing.ProductName.ShouldBe("Draft Product"); // Unchanged — original from CreateListing
    }

    [Fact]
    public async Task ContentPropagated_ToPausedListing_IsIgnored()
    {
        // Arrange
        await _fixture.CleanAllDocumentsAsync();
        var sku = "CONTENT-PAUSED-001";
        await SeedProductSummaryAsync(sku, "Paused Product");

        // Create, activate, then pause
        await _fixture.ExecuteAndWaitAsync(new CreateListing(sku, "OWN_WEBSITE", "Original content"));
        var listingId = ListingStreamId.Compute(sku, "OWN_WEBSITE");
        await _fixture.ExecuteAndWaitAsync(new ActivateListing(listingId));
        await _fixture.ExecuteAndWaitAsync(new PauseListing(listingId, "Temp pause"));

        // Act: Propagate content update
        var contentUpdate = new ProductContentUpdated(
            sku,
            "Updated Product Name",
            "Updated product description",
            DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(contentUpdate);

        // Assert: Paused listing content is NOT updated
        using var session = _fixture.GetDocumentSession();
        var listing = await session.Events.AggregateStreamAsync<Listing.Listing>(listingId);
        listing.ShouldNotBeNull();
        listing.Status.ShouldBe(ListingStatus.Paused);
        listing.ProductName.ShouldBe("Paused Product"); // Unchanged
    }
}
