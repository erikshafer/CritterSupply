using Listings.Listing;
using Listings.ProductSummary;
using Messages.Contracts.Marketplaces;

namespace Listings.Api.IntegrationTests;

/// <summary>
/// Tests for <see cref="MarketplaceListingActivatedHandler"/>.
/// Verifies Submitted → Live transition and idempotency guards.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class MarketplaceListingActivatedHandlerTests
{
    private readonly TestFixture _fixture;

    public MarketplaceListingActivatedHandlerTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task SeedProductSummaryAsync(string sku, string name = "Test Product")
    {
        using var session = _fixture.GetDocumentSession();
        session.Store(new ProductSummaryView
        {
            Id = sku,
            Name = name,
            Description = "A test product",
            Category = "Dogs",
            Status = ProductSummaryStatus.Active,
            Brand = "TestBrand",
            HasDimensions = false,
            ImageUrls = []
        });
        await session.SaveChangesAsync();
    }

    private async Task<Guid> SeedSubmittedListingAsync(string sku, string channelCode)
    {
        await SeedProductSummaryAsync(sku);
        await _fixture.ExecuteAndWaitAsync(new CreateListing(sku, channelCode, null));

        var listingId = ListingStreamId.Compute(sku, channelCode);
        await _fixture.ExecuteAndWaitAsync(new SubmitListingForReview(listingId));
        await _fixture.ExecuteAndWaitAsync(new ApproveListing(listingId));
        return listingId;
    }

    [Fact]
    public async Task MarketplaceListingActivated_TransitionsToLive_WhenSubmitted()
    {
        // Arrange
        await _fixture.CleanAllDocumentsAsync();
        var sku = "MKT-ACT-001";
        var channelCode = "AMAZON_US";
        var listingId = await SeedSubmittedListingAsync(sku, channelCode);

        var integrationMessage = new MarketplaceListingActivated(
            listingId, sku, channelCode, "amzn-ext-001", DateTimeOffset.UtcNow);

        // Act
        var tracked = await _fixture.ExecuteAndWaitAsync(integrationMessage);

        // Assert: listing transitioned to Live
        using var session = _fixture.GetDocumentSession();
        var listing = await session.Events.AggregateStreamAsync<Listing.Listing>(listingId);
        listing.ShouldNotBeNull();
        listing.Status.ShouldBe(ListingStatus.Live);
        listing.ActivatedAt.ShouldNotBeNull();

        // Assert: ListingActivated integration message published
        var outbound = tracked.Sent.MessagesOf<Messages.Contracts.Listings.ListingActivated>().ToList();
        outbound.Count.ShouldBe(1);
        outbound[0].ListingId.ShouldBe(listingId);
        outbound[0].ChannelCode.ShouldBe(channelCode);
    }

    [Fact]
    public async Task MarketplaceListingActivated_IsNoOp_WhenAlreadyLive()
    {
        // Arrange — listing is already in Live state
        await _fixture.CleanAllDocumentsAsync();
        var sku = "MKT-ACT-002";
        var channelCode = "OWN_WEBSITE";
        await SeedProductSummaryAsync(sku);
        await _fixture.ExecuteAndWaitAsync(new CreateListing(sku, channelCode, null));
        var listingId = ListingStreamId.Compute(sku, channelCode);
        await _fixture.ExecuteAndWaitAsync(new ActivateListing(listingId)); // already Live

        using var sessionBefore = _fixture.GetDocumentSession();
        var eventsBefore = await sessionBefore.Events.FetchStreamAsync(listingId);
        var countBefore = eventsBefore.Count;

        var integrationMessage = new MarketplaceListingActivated(
            listingId, sku, channelCode, "own-web-ext", DateTimeOffset.UtcNow);

        // Act
        await _fixture.ExecuteAndWaitAsync(integrationMessage);

        // Assert: no new events appended (idempotent)
        using var sessionAfter = _fixture.GetDocumentSession();
        var eventsAfter = await sessionAfter.Events.FetchStreamAsync(listingId);
        eventsAfter.Count.ShouldBe(countBefore);

        var listing = await sessionAfter.Events.AggregateStreamAsync<Listing.Listing>(listingId);
        listing!.Status.ShouldBe(ListingStatus.Live);
    }

    [Fact]
    public async Task MarketplaceListingActivated_IsNoOp_WhenListingNotFound()
    {
        // Arrange — fire with a non-existent ListingId
        await _fixture.CleanAllDocumentsAsync();
        var nonExistentId = Guid.NewGuid();

        var integrationMessage = new MarketplaceListingActivated(
            nonExistentId, "GHOST-SKU", "AMAZON_US", "amzn-ghost", DateTimeOffset.UtcNow);

        // Act — should not throw
        var exception = await Record.ExceptionAsync(() =>
            _fixture.ExecuteAndWaitAsync(integrationMessage));

        exception.ShouldBeNull();
    }
}
