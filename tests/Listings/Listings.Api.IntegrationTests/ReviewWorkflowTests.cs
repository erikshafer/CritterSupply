using Listings.Listing;
using Listings.ProductSummary;

namespace Listings.Api.IntegrationTests;

/// <summary>
/// Tests the review/approval workflow: Draft → ReadyForReview → Submitted.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class ReviewWorkflowTests
{
    private readonly TestFixture _fixture;

    public ReviewWorkflowTests(TestFixture fixture)
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
    public async Task SubmitForReview_FromDraft_TransitionsToReadyForReview()
    {
        // Arrange
        await _fixture.CleanAllDocumentsAsync();
        var sku = "REVIEW-001";
        await SeedProductSummaryAsync(sku, "Review Product");

        await _fixture.ExecuteAndWaitAsync(new CreateListing(sku, "AMAZON_US", null));
        var listingId = ListingStreamId.Compute(sku, "AMAZON_US");

        // Act
        await _fixture.ExecuteAndWaitAsync(new SubmitListingForReview(listingId));

        // Assert
        using var session = _fixture.GetDocumentSession();
        var listing = await session.Events.AggregateStreamAsync<Listing.Listing>(listingId);
        listing.ShouldNotBeNull();
        listing.Status.ShouldBe(ListingStatus.ReadyForReview);
    }

    [Fact]
    public async Task SubmitForReview_FromLive_ReturnsDomainError()
    {
        // Arrange
        await _fixture.CleanAllDocumentsAsync();
        var sku = "REVIEW-002";
        await SeedProductSummaryAsync(sku, "Live Product");

        await _fixture.ExecuteAndWaitAsync(new CreateListing(sku, "OWN_WEBSITE", null));
        var listingId = ListingStreamId.Compute(sku, "OWN_WEBSITE");
        await _fixture.ExecuteAndWaitAsync(new ActivateListing(listingId));

        // Act & Assert
        await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            await _fixture.ExecuteAndWaitAsync(new SubmitListingForReview(listingId));
        });

        // Verify listing is still Live
        using var session = _fixture.GetDocumentSession();
        var listing = await session.Events.AggregateStreamAsync<Listing.Listing>(listingId);
        listing.ShouldNotBeNull();
        listing.Status.ShouldBe(ListingStatus.Live);
    }

    [Fact]
    public async Task ApproveListing_FromReadyForReview_TransitionsToSubmitted()
    {
        // Arrange
        await _fixture.CleanAllDocumentsAsync();
        var sku = "APPROVE-001";
        await SeedProductSummaryAsync(sku, "Approvable Product");

        await _fixture.ExecuteAndWaitAsync(new CreateListing(sku, "AMAZON_US", null));
        var listingId = ListingStreamId.Compute(sku, "AMAZON_US");
        await _fixture.ExecuteAndWaitAsync(new SubmitListingForReview(listingId));

        // Act
        await _fixture.ExecuteAndWaitAsync(new ApproveListing(listingId));

        // Assert
        using var session = _fixture.GetDocumentSession();
        var listing = await session.Events.AggregateStreamAsync<Listing.Listing>(listingId);
        listing.ShouldNotBeNull();
        listing.Status.ShouldBe(ListingStatus.Submitted);
    }

    [Fact]
    public async Task ApproveListing_FromDraft_ReturnsDomainError()
    {
        // Arrange
        await _fixture.CleanAllDocumentsAsync();
        var sku = "APPROVE-002";
        await SeedProductSummaryAsync(sku, "Unapproved Product");

        await _fixture.ExecuteAndWaitAsync(new CreateListing(sku, "AMAZON_US", null));
        var listingId = ListingStreamId.Compute(sku, "AMAZON_US");

        // Act & Assert
        await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            await _fixture.ExecuteAndWaitAsync(new ApproveListing(listingId));
        });

        // Verify listing is still Draft
        using var session = _fixture.GetDocumentSession();
        var listing = await session.Events.AggregateStreamAsync<Listing.Listing>(listingId);
        listing.ShouldNotBeNull();
        listing.Status.ShouldBe(ListingStatus.Draft);
    }

    [Fact]
    public async Task FullReviewFlow_Draft_ReadyForReview_Submitted_Live()
    {
        // Arrange
        await _fixture.CleanAllDocumentsAsync();
        var sku = "FULL-FLOW-001";
        await SeedProductSummaryAsync(sku, "Full Flow Product");

        await _fixture.ExecuteAndWaitAsync(new CreateListing(sku, "AMAZON_US", "Full flow content"));
        var listingId = ListingStreamId.Compute(sku, "AMAZON_US");

        // Act: Draft → ReadyForReview
        await _fixture.ExecuteAndWaitAsync(new SubmitListingForReview(listingId));

        // Act: ReadyForReview → Submitted
        await _fixture.ExecuteAndWaitAsync(new ApproveListing(listingId));

        // Act: Submitted → Live
        await _fixture.ExecuteAndWaitAsync(new ActivateListing(listingId));

        // Assert: Full lifecycle completed
        using var session = _fixture.GetDocumentSession();
        var listing = await session.Events.AggregateStreamAsync<Listing.Listing>(listingId);
        listing.ShouldNotBeNull();
        listing.Status.ShouldBe(ListingStatus.Live);
        listing.ActivatedAt.ShouldNotBeNull();
        listing.ChannelCode.ShouldBe("AMAZON_US");

        // Verify event stream contains all transitions
        var events = await session.Events.FetchStreamAsync(listingId);
        events.Count.ShouldBe(4); // Created, SubmittedForReview, Approved, Activated
    }
}
