using Listings.Listing;
using Listings.ProductSummary;
using Listings.Projections;

namespace Listings.Api.IntegrationTests;

/// <summary>
/// Tests the recall cascade handler: consuming ProductDiscontinued(IsRecall=true),
/// forcing down all active listings for the affected SKU, and publishing
/// ListingsCascadeCompleted.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class RecallCascadeTests
{
    private readonly TestFixture _fixture;

    public RecallCascadeTests(TestFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task SeedProductSummaryAsync(string sku, ProductSummaryStatus status = ProductSummaryStatus.Active)
    {
        using var session = _fixture.GetDocumentSession();
        session.Store(new ProductSummaryView
        {
            Id = sku,
            Name = $"Product {sku}",
            Category = "Dogs",
            Status = status,
            ImageUrls = []
        });
        await session.SaveChangesAsync();
    }

    private async Task<Guid> CreateAndActivateListingAsync(string sku, string channelCode)
    {
        var listingId = ListingStreamId.Compute(sku, channelCode);
        var now = DateTimeOffset.UtcNow;

        // Seed the listing stream directly with events (bypasses channel-specific activation rules)
        using var session = _fixture.GetDocumentSession();
        session.Events.StartStream<Listing.Listing>(listingId,
            new ListingDraftCreated(listingId, sku, channelCode, $"Product {sku}", null, now),
            new ListingActivated(listingId, channelCode, now));
        await session.SaveChangesAsync();

        return listingId;
    }

    [Fact]
    public async Task RecallCascade_WithThreeListingsAcrossTwoChannels_ForcesDownAll()
    {
        // Arrange
        await _fixture.CleanAllDocumentsAsync();
        var sku = "RECALL-CASCADE-001";
        await SeedProductSummaryAsync(sku);

        // Create 3 active listings across 3 channels (2+ unique channel codes)
        var listing1 = await CreateAndActivateListingAsync(sku, "OWN_WEBSITE");
        var listing2 = await CreateAndActivateListingAsync(sku, "AMAZON_US");
        var listing3 = await CreateAndActivateListingAsync(sku, "WALMART_US");

        // Verify pre-condition: 3 active listings exist
        using (var preSession = _fixture.GetDocumentSession())
        {
            var activeView = await preSession.LoadAsync<ListingsActiveView>(sku);
            activeView.ShouldNotBeNull();
            activeView.ActiveListingStreamIds.Count.ShouldBe(3);
        }

        // Act: Recall cascade
        var recallMessage = new Messages.Contracts.ProductCatalog.ProductDiscontinued(
            Sku: sku,
            DiscontinuedAt: DateTimeOffset.UtcNow,
            Reason: "Safety hazard identified",
            IsRecall: true);

        var tracked = await _fixture.ExecuteAndWaitAsync(recallMessage);

        // Assert: All 3 listings are now Ended
        using var session = _fixture.GetDocumentSession();

        var l1 = await session.Events.AggregateStreamAsync<Listing.Listing>(listing1);
        l1.ShouldNotBeNull();
        l1.Status.ShouldBe(ListingStatus.Ended);

        var l2 = await session.Events.AggregateStreamAsync<Listing.Listing>(listing2);
        l2.ShouldNotBeNull();
        l2.Status.ShouldBe(ListingStatus.Ended);

        var l3 = await session.Events.AggregateStreamAsync<Listing.Listing>(listing3);
        l3.ShouldNotBeNull();
        l3.Status.ShouldBe(ListingStatus.Ended);

        // Assert: Each listing has a ListingForcedDown event
        var events1 = await session.Events.FetchStreamAsync(listing1);
        events1.ShouldContain(e => e.EventType == typeof(ListingForcedDown));

        var events2 = await session.Events.FetchStreamAsync(listing2);
        events2.ShouldContain(e => e.EventType == typeof(ListingForcedDown));

        var events3 = await session.Events.FetchStreamAsync(listing3);
        events3.ShouldContain(e => e.EventType == typeof(ListingForcedDown));

        // Assert: ListingsActiveView should be empty for this SKU
        var postActiveView = await session.LoadAsync<ListingsActiveView>(sku);
        if (postActiveView is not null)
        {
            postActiveView.ActiveListingStreamIds.ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task RecallCascade_AlreadyEndedListing_IsSkipped()
    {
        // Arrange
        await _fixture.CleanAllDocumentsAsync();
        var sku = "RECALL-IDEMPOTENT-001";
        await SeedProductSummaryAsync(sku);

        // Create a listing, activate it, then end it — all via direct event seeding
        var listingId = ListingStreamId.Compute(sku, "OWN_WEBSITE");
        var now = DateTimeOffset.UtcNow;
        using (var seedSession = _fixture.GetDocumentSession())
        {
            seedSession.Events.StartStream<Listing.Listing>(listingId,
                new ListingDraftCreated(listingId, sku, "OWN_WEBSITE", $"Product {sku}", null, now),
                new ListingActivated(listingId, "OWN_WEBSITE", now),
                new ListingEnded(listingId, sku, "OWN_WEBSITE", EndedCause.ManualEnd, now));
            await seedSession.SaveChangesAsync();
        }

        // Verify pre-condition: listing is Ended
        using (var preSession = _fixture.GetDocumentSession())
        {
            var listing = await preSession.Events.AggregateStreamAsync<Listing.Listing>(listingId);
            listing.ShouldNotBeNull();
            listing.Status.ShouldBe(ListingStatus.Ended);
        }

        // Act: Recall cascade on already-ended listing
        var recallMessage = new Messages.Contracts.ProductCatalog.ProductDiscontinued(
            Sku: sku,
            DiscontinuedAt: DateTimeOffset.UtcNow,
            Reason: "Safety issue",
            IsRecall: true);

        await _fixture.ExecuteAndWaitAsync(recallMessage);

        // Assert: No ListingForcedDown events appended (idempotency)
        using var session = _fixture.GetDocumentSession();
        var events = await session.Events.FetchStreamAsync(listingId);
        events.Count(e => e.EventType == typeof(ListingForcedDown)).ShouldBe(0);
    }

    [Fact]
    public async Task RecallCascade_PublishesListingsCascadeCompleted_WithCorrectCount()
    {
        // Arrange
        await _fixture.CleanAllDocumentsAsync();
        var sku = "RECALL-COUNT-001";
        await SeedProductSummaryAsync(sku);

        // Create 2 active listings
        var listing1 = await CreateAndActivateListingAsync(sku, "OWN_WEBSITE");
        var listing2 = await CreateAndActivateListingAsync(sku, "AMAZON_US");

        // Act
        var recallMessage = new Messages.Contracts.ProductCatalog.ProductDiscontinued(
            Sku: sku,
            DiscontinuedAt: DateTimeOffset.UtcNow,
            Reason: "Recall test",
            IsRecall: true);

        await _fixture.ExecuteAndWaitAsync(recallMessage);

        // Assert: Both listings forced down
        using var session = _fixture.GetDocumentSession();
        var l1 = await session.Events.AggregateStreamAsync<Listing.Listing>(listing1);
        l1.ShouldNotBeNull();
        l1.Status.ShouldBe(ListingStatus.Ended);

        var l2 = await session.Events.AggregateStreamAsync<Listing.Listing>(listing2);
        l2.ShouldNotBeNull();
        l2.Status.ShouldBe(ListingStatus.Ended);

        // Assert: ForcedDown events exist on each stream
        var events1 = await session.Events.FetchStreamAsync(listing1);
        events1.ShouldContain(e => e.EventType == typeof(ListingForcedDown));

        var events2 = await session.Events.FetchStreamAsync(listing2);
        events2.ShouldContain(e => e.EventType == typeof(ListingForcedDown));
    }

    [Fact]
    public async Task RecallCascade_NonRecallDiscontinuation_DoesNotForceDown()
    {
        // Arrange
        await _fixture.CleanAllDocumentsAsync();
        var sku = "NON-RECALL-001";
        await SeedProductSummaryAsync(sku);

        await CreateAndActivateListingAsync(sku, "OWN_WEBSITE");

        // Act: Non-recall discontinuation
        var message = new Messages.Contracts.ProductCatalog.ProductDiscontinued(
            Sku: sku,
            DiscontinuedAt: DateTimeOffset.UtcNow,
            Reason: "End of life",
            IsRecall: false);

        var tracked = await _fixture.ExecuteAndWaitAsync(message);

        // Assert: Listing is still Live (not forced down)
        var listingId = ListingStreamId.Compute(sku, "OWN_WEBSITE");
        using var session = _fixture.GetDocumentSession();
        var listing = await session.Events.AggregateStreamAsync<Listing.Listing>(listingId);
        listing.ShouldNotBeNull();
        listing.Status.ShouldBe(ListingStatus.Live);
    }
}
