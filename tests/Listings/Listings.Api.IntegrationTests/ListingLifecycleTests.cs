using Listings.Listing;
using Listings.ProductSummary;
using Listings.Projections;

namespace Listings.Api.IntegrationTests;

/// <summary>
/// Tests the Listing aggregate lifecycle: create, activate, pause, resume, end.
/// Uses direct Wolverine handler invocation via ExecuteAndWaitAsync.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class ListingLifecycleTests
{
    private readonly TestFixture _fixture;

    public ListingLifecycleTests(TestFixture fixture)
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
            Description = "A test product",
            Category = "Dogs",
            Status = status,
            Brand = "TestBrand",
            HasDimensions = false,
            ImageUrls = []
        });
        await session.SaveChangesAsync();
    }

    [Fact]
    public async Task CreateListing_ForExistingProduct_CreatesDraftListing()
    {
        // Arrange
        await _fixture.CleanAllDocumentsAsync();
        var sku = "DOG-BOWL-001";
        await SeedProductSummaryAsync(sku, "Premium Dog Bowl");

        var command = new CreateListing(sku, "AMAZON_US", "Initial listing content");

        // Act
        await _fixture.ExecuteAndWaitAsync(command);

        // Assert: Listing aggregate is in Draft state
        var listingId = ListingStreamId.Compute(sku, "AMAZON_US");
        using var session = _fixture.GetDocumentSession();
        var listing = await session.Events.AggregateStreamAsync<Listing.Listing>(listingId);

        listing.ShouldNotBeNull();
        listing.Status.ShouldBe(ListingStatus.Draft);
        listing.Sku.ShouldBe(sku);
        listing.ChannelCode.ShouldBe("AMAZON_US");
        listing.ProductName.ShouldBe("Premium Dog Bowl");

        // Assert: ListingsActiveView updated
        var activeView = await session.LoadAsync<ListingsActiveView>(sku);
        activeView.ShouldNotBeNull();
        activeView.ActiveListingStreamIds.ShouldContain(listingId);
    }

    [Fact]
    public async Task CreateListing_ForNonExistentProduct_ThrowsError()
    {
        // Arrange
        await _fixture.CleanAllDocumentsAsync();

        var command = new CreateListing("NONEXISTENT-SKU", "AMAZON_US", null);

        // Act & Assert: Creating a listing for a non-existent product should throw
        await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            await _fixture.ExecuteAndWaitAsync(command);
        });

        // Verify no listing was created
        var listingId = ListingStreamId.Compute("NONEXISTENT-SKU", "AMAZON_US");
        using var session = _fixture.GetDocumentSession();
        var listing = await session.Events.AggregateStreamAsync<Listing.Listing>(listingId);
        listing.ShouldBeNull();
    }

    [Fact]
    public async Task CreateListing_ForDiscontinuedProduct_ThrowsError()
    {
        // Arrange
        await _fixture.CleanAllDocumentsAsync();
        var sku = "DISC-PRODUCT-001";
        await SeedProductSummaryAsync(sku, status: ProductSummaryStatus.Discontinued);

        var command = new CreateListing(sku, "AMAZON_US", null);

        // Act & Assert
        await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            await _fixture.ExecuteAndWaitAsync(command);
        });

        // Verify no listing was created
        var listingId = ListingStreamId.Compute(sku, "AMAZON_US");
        using var session = _fixture.GetDocumentSession();
        var listing = await session.Events.AggregateStreamAsync<Listing.Listing>(listingId);
        listing.ShouldBeNull();
    }

    [Fact]
    public async Task CreateListing_DuplicateSkuChannel_ThrowsError()
    {
        // Arrange
        await _fixture.CleanAllDocumentsAsync();
        var sku = "DUP-SKU-001";
        await SeedProductSummaryAsync(sku, "Duplicate Test Product");

        // Create first listing (should succeed)
        await _fixture.ExecuteAndWaitAsync(new CreateListing(sku, "AMAZON_US", null));

        // Act & Assert: try to create duplicate — should throw
        await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            await _fixture.ExecuteAndWaitAsync(new CreateListing(sku, "AMAZON_US", "dupe"));
        });

        // Verify only one listing exists
        var listingId = ListingStreamId.Compute(sku, "AMAZON_US");
        using var session = _fixture.GetDocumentSession();
        var events = await session.Events.FetchStreamAsync(listingId);
        // Only one ListingDraftCreated event should exist
        events.Count(e => e.EventType == typeof(ListingDraftCreated)).ShouldBe(1);
    }

    [Fact]
    public async Task ActivateListing_FromDraft_OwnWebsite_TransitionsToLive()
    {
        // Arrange
        await _fixture.CleanAllDocumentsAsync();
        var sku = "OWN-WEB-001";
        await SeedProductSummaryAsync(sku, "Own Website Product");

        // Create a Draft listing for OWN_WEBSITE
        await _fixture.ExecuteAndWaitAsync(new CreateListing(sku, "OWN_WEBSITE", null));

        var listingId = ListingStreamId.Compute(sku, "OWN_WEBSITE");

        // Act: Activate (Draft → Live via OWN_WEBSITE fast path)
        await _fixture.ExecuteAndWaitAsync(new ActivateListing(listingId));

        // Assert
        using var session = _fixture.GetDocumentSession();
        var listing = await session.Events.AggregateStreamAsync<Listing.Listing>(listingId);
        listing.ShouldNotBeNull();
        listing.Status.ShouldBe(ListingStatus.Live);
        listing.ActivatedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task PauseListing_FromLive_TransitionsToPaused()
    {
        // Arrange
        await _fixture.CleanAllDocumentsAsync();
        var sku = "PAUSE-001";
        await SeedProductSummaryAsync(sku, "Pausable Product");

        await _fixture.ExecuteAndWaitAsync(new CreateListing(sku, "OWN_WEBSITE", null));
        var listingId = ListingStreamId.Compute(sku, "OWN_WEBSITE");
        await _fixture.ExecuteAndWaitAsync(new ActivateListing(listingId));

        // Act: Pause
        await _fixture.ExecuteAndWaitAsync(new PauseListing(listingId, "Out of stock"));

        // Assert
        using var session = _fixture.GetDocumentSession();
        var listing = await session.Events.AggregateStreamAsync<Listing.Listing>(listingId);
        listing.ShouldNotBeNull();
        listing.Status.ShouldBe(ListingStatus.Paused);
        listing.PauseReason.ShouldBe("Out of stock");
    }

    [Fact]
    public async Task ResumeListing_FromPaused_TransitionsToLive()
    {
        // Arrange
        await _fixture.CleanAllDocumentsAsync();
        var sku = "RESUME-001";
        await SeedProductSummaryAsync(sku, "Resumable Product");

        await _fixture.ExecuteAndWaitAsync(new CreateListing(sku, "OWN_WEBSITE", null));
        var listingId = ListingStreamId.Compute(sku, "OWN_WEBSITE");
        await _fixture.ExecuteAndWaitAsync(new ActivateListing(listingId));
        await _fixture.ExecuteAndWaitAsync(new PauseListing(listingId, "Temporarily paused"));

        // Act: Resume
        await _fixture.ExecuteAndWaitAsync(new ResumeListing(listingId));

        // Assert
        using var session = _fixture.GetDocumentSession();
        var listing = await session.Events.AggregateStreamAsync<Listing.Listing>(listingId);
        listing.ShouldNotBeNull();
        listing.Status.ShouldBe(ListingStatus.Live);
        listing.PauseReason.ShouldBeNull();
    }

    [Fact]
    public async Task EndListing_Manual_TransitionsToEnded()
    {
        // Arrange
        await _fixture.CleanAllDocumentsAsync();
        var sku = "END-001";
        await SeedProductSummaryAsync(sku, "Endable Product");

        await _fixture.ExecuteAndWaitAsync(new CreateListing(sku, "OWN_WEBSITE", null));
        var listingId = ListingStreamId.Compute(sku, "OWN_WEBSITE");
        await _fixture.ExecuteAndWaitAsync(new ActivateListing(listingId));

        // Act: End listing
        await _fixture.ExecuteAndWaitAsync(new EndListing(listingId));

        // Assert
        using var session = _fixture.GetDocumentSession();
        var listing = await session.Events.AggregateStreamAsync<Listing.Listing>(listingId);
        listing.ShouldNotBeNull();
        listing.Status.ShouldBe(ListingStatus.Ended);
        listing.EndCause.ShouldBe(EndedCause.ManualEnd);
        listing.IsTerminal.ShouldBeTrue();

        // Assert: ListingsActiveView should no longer contain this listing
        var activeView = await session.LoadAsync<ListingsActiveView>(sku);
        if (activeView is not null)
        {
            activeView.ActiveListingStreamIds.ShouldNotContain(listingId);
        }
    }

    [Fact]
    public async Task InvalidTransition_EndedToLive_ThrowsError()
    {
        // Arrange
        await _fixture.CleanAllDocumentsAsync();
        var sku = "INVALID-001";
        await SeedProductSummaryAsync(sku, "Invalid Transition Product");

        await _fixture.ExecuteAndWaitAsync(new CreateListing(sku, "OWN_WEBSITE", null));
        var listingId = ListingStreamId.Compute(sku, "OWN_WEBSITE");
        await _fixture.ExecuteAndWaitAsync(new ActivateListing(listingId));
        await _fixture.ExecuteAndWaitAsync(new EndListing(listingId));

        // Act & Assert: try to activate an ended listing — should throw
        await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            await _fixture.ExecuteAndWaitAsync(new ActivateListing(listingId));
        });

        // Assert: listing should still be ended
        using var session = _fixture.GetDocumentSession();
        var listing = await session.Events.AggregateStreamAsync<Listing.Listing>(listingId);
        listing.ShouldNotBeNull();
        listing.Status.ShouldBe(ListingStatus.Ended);
    }
}
