using System.Net;
using Listings.Listing;
using Listings.ProductSummary;
using Marten;
using Shouldly;
using Wolverine.Tracking;

namespace Listings.Api.IntegrationTests;

/// <summary>
/// Tests HTTP endpoints for listings via Alba.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class ListingEndpointTests
{
    private readonly TestFixture _fixture;

    public ListingEndpointTests(TestFixture fixture)
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
    public async Task POST_CreateListing_Returns201_WithListingId()
    {
        // Arrange
        await _fixture.CleanAllDocumentsAsync();
        var sku = "EP-CREATE-001";
        await SeedProductSummaryAsync(sku, "Endpoint Create Product");

        // Act
        var (_, result) = await _fixture.TrackedHttpCall(s =>
        {
            s.Post.Json(new { Sku = sku, ChannelCode = "OWN_WEBSITE", InitialContent = "test content" })
                .ToUrl("/api/listings");
            s.StatusCodeShouldBe(201);
        });

        // Assert
        var body = result.ReadAsJson<CreateListingResponse>();
        body.ShouldNotBeNull();
        body.Sku.ShouldBe(sku);
        body.ChannelCode.ShouldBe("OWN_WEBSITE");
        body.ListingId.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task POST_SubmitForReview_Returns200()
    {
        // Arrange
        await _fixture.CleanAllDocumentsAsync();
        var sku = "EP-SUBMIT-001";
        await SeedProductSummaryAsync(sku, "Submit Endpoint Product");

        await _fixture.ExecuteAndWaitAsync(new CreateListing(sku, "AMAZON_US", null));
        var listingId = ListingStreamId.Compute(sku, "AMAZON_US");

        // Act
        await _fixture.TrackedHttpCall(s =>
        {
            s.Post.Json(new { }).ToUrl($"/api/listings/{listingId}/submit-for-review");
            s.StatusCodeShouldBe(200);
        });

        // Assert
        using var session = _fixture.GetDocumentSession();
        var listing = await session.Events.AggregateStreamAsync<Listing.Listing>(listingId);
        listing.ShouldNotBeNull();
        listing.Status.ShouldBe(ListingStatus.ReadyForReview);
    }

    [Fact]
    public async Task POST_ApproveListing_Returns200()
    {
        // Arrange
        await _fixture.CleanAllDocumentsAsync();
        var sku = "EP-APPROVE-001";
        await SeedProductSummaryAsync(sku, "Approve Endpoint Product");

        await _fixture.ExecuteAndWaitAsync(new CreateListing(sku, "AMAZON_US", null));
        var listingId = ListingStreamId.Compute(sku, "AMAZON_US");
        await _fixture.ExecuteAndWaitAsync(new SubmitListingForReview(listingId));

        // Act
        await _fixture.TrackedHttpCall(s =>
        {
            s.Post.Json(new { }).ToUrl($"/api/listings/{listingId}/approve");
            s.StatusCodeShouldBe(200);
        });

        // Assert
        using var session = _fixture.GetDocumentSession();
        var listing = await session.Events.AggregateStreamAsync<Listing.Listing>(listingId);
        listing.ShouldNotBeNull();
        listing.Status.ShouldBe(ListingStatus.Submitted);
    }

    [Fact]
    public async Task GET_GetListing_Returns200_WithCurrentState()
    {
        // Arrange
        await _fixture.CleanAllDocumentsAsync();
        var sku = "EP-GET-001";
        await SeedProductSummaryAsync(sku, "Get Endpoint Product");

        await _fixture.ExecuteAndWaitAsync(new CreateListing(sku, "OWN_WEBSITE", "test content"));
        var listingId = ListingStreamId.Compute(sku, "OWN_WEBSITE");

        // Act
        var result = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url($"/api/listings/{listingId}");
            s.StatusCodeShouldBe(200);
        });

        // Assert
        var body = result.ReadAsJson<ListingResponseDto>();
        body.ShouldNotBeNull();
        body.ListingId.ShouldBe(listingId);
        body.Sku.ShouldBe(sku);
        body.Status.ShouldBe("Draft");
        body.ProductName.ShouldBe("Get Endpoint Product");
    }

    [Fact]
    public async Task GET_ListListings_BySku_ReturnsMatchingListings()
    {
        // Arrange
        await _fixture.CleanAllDocumentsAsync();
        var sku = "EP-LIST-001";
        await SeedProductSummaryAsync(sku, "List Endpoint Product");

        await _fixture.ExecuteAndWaitAsync(new CreateListing(sku, "OWN_WEBSITE", null));
        await _fixture.ExecuteAndWaitAsync(new CreateListing(sku, "AMAZON_US", null));

        // Act
        var result = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url($"/api/listings?sku={sku}");
            s.StatusCodeShouldBe(200);
        });

        // Assert
        var listings = result.ReadAsJson<ListingResponseDto[]>();
        listings.ShouldNotBeNull();
        listings.Length.ShouldBe(2);
        listings.ShouldAllBe(l => l.Sku == sku);
    }

    [Fact]
    public async Task GET_NonExistentListing_Returns404()
    {
        // Verify that requesting a non-existent listing returns 404,
        // confirming the auth pipeline is active and the endpoint is functional.
        var nonExistentId = Guid.CreateVersion7();
        var result = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url($"/api/listings/{nonExistentId}");
            s.StatusCodeShouldBe(404);
        });
    }

    // Local DTO for deserializing responses
    private sealed record ListingResponseDto(
        Guid ListingId,
        string Sku,
        string ChannelCode,
        string ProductName,
        string? Content,
        string Status,
        DateTimeOffset CreatedAt,
        DateTimeOffset? ActivatedAt,
        DateTimeOffset? EndedAt,
        string? EndCause,
        string? PauseReason);

    // Local DTO for paginated response
    private sealed record PaginatedListingsResponseDto(
        List<ListingResponseDto> Items,
        int TotalCount,
        int Page,
        int PageSize);

    [Fact]
    public async Task GET_ListAllListings_ReturnsAllListings_Paginated()
    {
        // Arrange
        await _fixture.CleanAllDocumentsAsync();
        var sku1 = "EP-LISTALL-001";
        var sku2 = "EP-LISTALL-002";
        await SeedProductSummaryAsync(sku1, "Paginated Product 1");
        await SeedProductSummaryAsync(sku2, "Paginated Product 2");

        await _fixture.ExecuteAndWaitAsync(new CreateListing(sku1, "OWN_WEBSITE", null));
        await _fixture.ExecuteAndWaitAsync(new CreateListing(sku2, "OWN_WEBSITE", null));
        await _fixture.ExecuteAndWaitAsync(new CreateListing(sku1, "AMAZON_US", null));

        // Act
        var result = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url("/api/listings/all?page=1&pageSize=25");
            s.StatusCodeShouldBe(200);
        });

        // Assert
        var body = result.ReadAsJson<PaginatedListingsResponseDto>();
        body.ShouldNotBeNull();
        body.TotalCount.ShouldBe(3);
        body.Page.ShouldBe(1);
        body.PageSize.ShouldBe(25);
        body.Items.Count.ShouldBe(3);
    }

    [Fact]
    public async Task GET_ListAllListings_WithStatusFilter_ReturnsFilteredResults()
    {
        // Arrange
        await _fixture.CleanAllDocumentsAsync();
        var sku = "EP-FILTER-001";
        await SeedProductSummaryAsync(sku, "Filter Product");

        // Create a Draft listing and then activate it to Live
        await _fixture.ExecuteAndWaitAsync(new CreateListing(sku, "OWN_WEBSITE", null));
        var listingId = ListingStreamId.Compute(sku, "OWN_WEBSITE");
        await _fixture.ExecuteAndWaitAsync(new ActivateListing(listingId));

        // Create another Draft listing
        await _fixture.ExecuteAndWaitAsync(new CreateListing(sku, "AMAZON_US", null));

        // Act — filter by Live status
        var result = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url("/api/listings/all?status=Live");
            s.StatusCodeShouldBe(200);
        });

        // Assert
        var body = result.ReadAsJson<PaginatedListingsResponseDto>();
        body.ShouldNotBeNull();
        body.TotalCount.ShouldBe(1);
        body.Items.ShouldAllBe(l => l.Status == "Live");
    }

    [Fact]
    public async Task GET_ListAllListings_WithPagination_RespectsPageSize()
    {
        // Arrange
        await _fixture.CleanAllDocumentsAsync();
        var sku = "EP-PAGE-001";
        await SeedProductSummaryAsync(sku, "Pagination Product");

        await _fixture.ExecuteAndWaitAsync(new CreateListing(sku, "OWN_WEBSITE", null));
        await _fixture.ExecuteAndWaitAsync(new CreateListing(sku, "AMAZON_US", null));
        await _fixture.ExecuteAndWaitAsync(new CreateListing(sku, "EBAY_US", null));

        // Act — page size of 2
        var result = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url("/api/listings/all?page=1&pageSize=2");
            s.StatusCodeShouldBe(200);
        });

        // Assert
        var body = result.ReadAsJson<PaginatedListingsResponseDto>();
        body.ShouldNotBeNull();
        body.TotalCount.ShouldBe(3);
        body.Items.Count.ShouldBe(2);
        body.PageSize.ShouldBe(2);
    }
}
