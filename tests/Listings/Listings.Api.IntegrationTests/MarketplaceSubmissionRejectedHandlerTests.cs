using Listings.Listing;
using Listings.ProductSummary;
using Messages.Contracts.Marketplaces;

namespace Listings.Api.IntegrationTests;

/// <summary>
/// Tests for <see cref="MarketplaceSubmissionRejectedHandler"/>.
/// Verifies Submitted → Ended (SubmissionRejected) transition and idempotency guards.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class MarketplaceSubmissionRejectedHandlerTests
{
    private readonly TestFixture _fixture;

    public MarketplaceSubmissionRejectedHandlerTests(TestFixture fixture)
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
    public async Task MarketplaceSubmissionRejected_TransitionsToEnded_WhenSubmitted()
    {
        // Arrange
        await _fixture.CleanAllDocumentsAsync();
        var sku = "MKT-REJ-001";
        var channelCode = "WALMART_US";
        var listingId = await SeedSubmittedListingAsync(sku, channelCode);

        var integrationMessage = new MarketplaceSubmissionRejected(
            listingId, sku, channelCode, "Feed processing timed out after 10 attempts", DateTimeOffset.UtcNow);

        // Act
        var tracked = await _fixture.ExecuteAndWaitAsync(integrationMessage);

        // Assert: listing transitioned to Ended with SubmissionRejected cause
        using var session = _fixture.GetDocumentSession();
        var listing = await session.Events.AggregateStreamAsync<Listing.Listing>(listingId);
        listing.ShouldNotBeNull();
        listing.Status.ShouldBe(ListingStatus.Ended);
        listing.EndCause.ShouldBe(EndedCause.SubmissionRejected);
        listing.EndedAt.ShouldNotBeNull();

        // Assert: ListingEnded integration message published
        var outbound = tracked.Sent.MessagesOf<Messages.Contracts.Listings.ListingEnded>().ToList();
        outbound.Count.ShouldBe(1);
        outbound[0].ListingId.ShouldBe(listingId);
        outbound[0].ChannelCode.ShouldBe(channelCode);
        outbound[0].Sku.ShouldBe(sku);
    }

    [Fact]
    public async Task MarketplaceSubmissionRejected_IsNoOp_WhenAlreadyEnded()
    {
        // Arrange — listing already in Ended state
        await _fixture.CleanAllDocumentsAsync();
        var sku = "MKT-REJ-002";
        var channelCode = "OWN_WEBSITE";
        await SeedProductSummaryAsync(sku);
        await _fixture.ExecuteAndWaitAsync(new CreateListing(sku, channelCode, null));
        var listingId = ListingStreamId.Compute(sku, channelCode);
        await _fixture.ExecuteAndWaitAsync(new ActivateListing(listingId));
        await _fixture.ExecuteAndWaitAsync(new EndListing(listingId)); // already Ended

        using var sessionBefore = _fixture.GetDocumentSession();
        var eventsBefore = await sessionBefore.Events.FetchStreamAsync(listingId);
        var countBefore = eventsBefore.Count;

        var integrationMessage = new MarketplaceSubmissionRejected(
            listingId, sku, channelCode, "Some rejection reason", DateTimeOffset.UtcNow);

        // Act
        await _fixture.ExecuteAndWaitAsync(integrationMessage);

        // Assert: no new events appended (idempotent)
        using var sessionAfter = _fixture.GetDocumentSession();
        var eventsAfter = await sessionAfter.Events.FetchStreamAsync(listingId);
        eventsAfter.Count.ShouldBe(countBefore);

        var listing = await sessionAfter.Events.AggregateStreamAsync<Listing.Listing>(listingId);
        listing!.Status.ShouldBe(ListingStatus.Ended);
    }

    [Fact]
    public async Task MarketplaceSubmissionRejected_IsNoOp_WhenListingNotFound()
    {
        // Arrange
        await _fixture.CleanAllDocumentsAsync();
        var nonExistentId = Guid.NewGuid();

        var integrationMessage = new MarketplaceSubmissionRejected(
            nonExistentId, "GHOST-SKU", "WALMART_US", "Rejection reason", DateTimeOffset.UtcNow);

        // Act — should not throw
        var exception = await Record.ExceptionAsync(() =>
            _fixture.ExecuteAndWaitAsync(integrationMessage));

        exception.ShouldBeNull();
    }
}
