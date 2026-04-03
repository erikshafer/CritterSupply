using System.Net;
using Marketplaces.Adapters;
using Microsoft.Extensions.Logging.Abstractions;
using static Marketplaces.Api.IntegrationTests.Helpers.MarketplaceAdapterTestHelpers;

namespace Marketplaces.Api.IntegrationTests;

/// <summary>
/// Tests for <see cref="WalmartMarketplaceAdapter"/> using fake HTTP handler and vault.
/// No real Walmart API calls are made — the handler intercepts all HTTP traffic.
/// </summary>
public sealed class WalmartMarketplaceAdapterTests : IDisposable
{
    private readonly FakeHttpMessageHandler _httpHandler = new();
    private readonly FakeVaultClient _vault = new();
    private readonly WalmartMarketplaceAdapter _adapter;

    public WalmartMarketplaceAdapterTests()
    {
        var httpClient = new HttpClient(_httpHandler);
        var factory = new FakeHttpClientFactory(httpClient);
        _adapter = new WalmartMarketplaceAdapter(
            _vault,
            factory,
            NullLogger<WalmartMarketplaceAdapter>.Instance);

        // Default vault secrets for all tests
        _vault.SetSecret("walmart/client-id", "test-walmart-client-id");
        _vault.SetSecret("walmart/client-secret", "test-walmart-client-secret");
        _vault.SetSecret("walmart/seller-id", "WALMART-SELLER-123");
    }

    [Fact]
    public void ChannelCode_IsWalmartUs()
    {
        _adapter.ChannelCode.ShouldBe("WALMART_US");
    }

    [Fact]
    public async Task SubmitListing_ReturnsSuccess_WhenApiReturns2xx()
    {
        // Arrange — OAuth token response
        _httpHandler.EnqueueResponse(HttpStatusCode.OK, new
        {
            access_token = "test-walmart-token",
            token_type = "Bearer",
            expires_in = 900
        });

        // Feed submission response
        _httpHandler.EnqueueResponse(HttpStatusCode.OK, new
        {
            feedId = "FEED-ABC-123"
        });

        var submission = CreateTestSubmission();

        // Act
        var result = await _adapter.SubmitListingAsync(submission);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.ExternalSubmissionId.ShouldNotBeNullOrEmpty();
        result.ExternalSubmissionId.ShouldStartWith("wmrt-");
        result.ExternalSubmissionId.ShouldContain("FEED-ABC-123");
    }

    [Fact]
    public async Task SubmitListing_ReturnsFailure_WhenApiReturns4xx()
    {
        // Arrange — OAuth token response (successful auth)
        _httpHandler.EnqueueResponse(HttpStatusCode.OK, new
        {
            access_token = "test-walmart-token",
            token_type = "Bearer",
            expires_in = 900
        });

        // Feed submission returns 400 Bad Request
        _httpHandler.EnqueueResponse(HttpStatusCode.BadRequest,
            new { errors = new[] { new { code = "INVALID_FEED", description = "Invalid item feed format" } } });

        var submission = CreateTestSubmission();

        // Act
        var result = await _adapter.SubmitListingAsync(submission);

        // Assert
        result.IsSuccess.ShouldBeFalse();
        result.ExternalSubmissionId.ShouldBeNull();
        result.ErrorMessage.ShouldNotBeNullOrEmpty();
        result.ErrorMessage.ShouldContain("400");
    }

    [Fact]
    public async Task SubmitListing_BuildsCorrectRequest()
    {
        // Arrange — OAuth token response
        _httpHandler.EnqueueResponse(HttpStatusCode.OK, new
        {
            access_token = "test-walmart-token",
            token_type = "Bearer",
            expires_in = 900
        });

        // Feed submission success
        _httpHandler.EnqueueResponse(HttpStatusCode.OK, new
        {
            feedId = "FEED-XYZ-789"
        });

        var submission = CreateTestSubmission();

        // Act
        await _adapter.SubmitListingAsync(submission);

        // Assert — verify the feed request (second request; first is token exchange)
        _httpHandler.SentRequests.Count.ShouldBe(2);

        var tokenRequest = _httpHandler.SentRequests[0];
        tokenRequest.RequestUri!.ToString().ShouldBe("https://marketplace.walmartapis.com/v3/token");
        tokenRequest.Method.ShouldBe(HttpMethod.Post);
        // Verify Basic auth header on token request
        tokenRequest.Headers.Authorization.ShouldNotBeNull();
        tokenRequest.Headers.Authorization!.Scheme.ShouldBe("Basic");

        var feedRequest = _httpHandler.SentRequests[1];
        feedRequest.Method.ShouldBe(HttpMethod.Post);
        feedRequest.RequestUri!.ToString().ShouldContain("/v3/feeds?feedType=MP_ITEM");

        // Verify required Walmart headers
        feedRequest.Headers.GetValues("WM_CONSUMER.ID").ShouldContain("WALMART-SELLER-123");
        feedRequest.Headers.GetValues("WM_SEC.ACCESS_TOKEN").ShouldContain("test-walmart-token");
        feedRequest.Headers.GetValues("WM_SVC.NAME").ShouldContain("Walmart Marketplace");
        feedRequest.Headers.TryGetValues("WM_QOS.CORRELATION_ID", out var correlationIds).ShouldBeTrue();
        correlationIds!.First().ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task SubmitListing_ReturnsFailure_WhenTokenExchangeFails()
    {
        // Arrange — Token exchange returns 401
        _httpHandler.EnqueueResponse(HttpStatusCode.Unauthorized,
            new { error = "invalid_client", error_description = "Client authentication failed" });

        var submission = CreateTestSubmission();

        // Act
        var result = await _adapter.SubmitListingAsync(submission);

        // Assert
        result.IsSuccess.ShouldBeFalse();
        result.ErrorMessage.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task SubmitListing_CachesToken_AcrossMultipleCalls()
    {
        // Arrange — only one token response (should be cached for second call)
        _httpHandler.EnqueueResponse(HttpStatusCode.OK, new
        {
            access_token = "cached-walmart-token",
            token_type = "Bearer",
            expires_in = 900
        });

        // Two feed submission responses
        _httpHandler.EnqueueResponse(HttpStatusCode.OK, new { feedId = "FEED-001" });
        _httpHandler.EnqueueResponse(HttpStatusCode.OK, new { feedId = "FEED-002" });

        // Act — two submissions
        var result1 = await _adapter.SubmitListingAsync(CreateTestSubmission("SKU-001"));
        var result2 = await _adapter.SubmitListingAsync(CreateTestSubmission("SKU-002"));

        // Assert — only 1 token request + 2 feed requests = 3 total (not 4)
        result1.IsSuccess.ShouldBeTrue();
        result2.IsSuccess.ShouldBeTrue();
        _httpHandler.SentRequests.Count.ShouldBe(3);

        // First request is token exchange, second and third are feed submissions
        _httpHandler.SentRequests[0].RequestUri!.ToString().ShouldContain("walmartapis.com/v3/token");
        _httpHandler.SentRequests[1].RequestUri!.ToString().ShouldContain("/v3/feeds");
        _httpHandler.SentRequests[2].RequestUri!.ToString().ShouldContain("/v3/feeds");
    }

    [Fact]
    public async Task CheckSubmissionStatus_ReturnsPendingStatus()
    {
        // Act — skeleton implementation, should return not-live/not-failed
        var status = await _adapter.CheckSubmissionStatusAsync("wmrt-FEED-123");

        // Assert
        status.ExternalSubmissionId.ShouldBe("wmrt-FEED-123");
        status.IsLive.ShouldBeFalse();
        status.IsFailed.ShouldBeFalse();
    }

    [Fact]
    public async Task DeactivateListing_ReturnsFalse()
    {
        // Act — skeleton implementation
        var result = await _adapter.DeactivateListingAsync("wmrt-listing-456");

        // Assert
        result.ShouldBeFalse();
    }

    private static ListingSubmission CreateTestSubmission(string sku = "SKU-001") =>
        new(
            ListingId: Guid.NewGuid(),
            Sku: sku,
            ChannelCode: "WALMART_US",
            ProductName: "Premium Critter Kibble",
            Description: "High-quality kibble for discerning critters",
            Category: "Pet Food",
            Price: 29.99m);

    public void Dispose()
    {
        _httpHandler.Dispose();
    }
}
