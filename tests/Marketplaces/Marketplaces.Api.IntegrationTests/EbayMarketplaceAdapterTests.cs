using System.Net;
using System.Text;
using Marketplaces.Adapters;
using Microsoft.Extensions.Logging.Abstractions;
using static Marketplaces.Api.IntegrationTests.Helpers.MarketplaceAdapterTestHelpers;

namespace Marketplaces.Api.IntegrationTests;

/// <summary>
/// Tests for <see cref="EbayMarketplaceAdapter"/> using fake HTTP handler and vault.
/// No real eBay API calls are made — the handler intercepts all HTTP traffic.
/// The eBay adapter has a two-step flow (create offer → publish offer), so tests
/// must mock multiple sequential HTTP calls with URL-keyed responses.
/// </summary>
public sealed class EbayMarketplaceAdapterTests : IDisposable
{
    private readonly FakeHttpMessageHandler _httpHandler = new();
    private readonly FakeVaultClient _vault = new();
    private readonly EbayMarketplaceAdapter _adapter;

    public EbayMarketplaceAdapterTests()
    {
        var httpClient = new HttpClient(_httpHandler);
        var factory = new FakeHttpClientFactory(httpClient);
        _adapter = new EbayMarketplaceAdapter(
            _vault,
            factory,
            NullLogger<EbayMarketplaceAdapter>.Instance);

        // Default vault secrets for all tests
        _vault.SetSecret("ebay/client-id", "test-ebay-client-id");
        _vault.SetSecret("ebay/client-secret", "test-ebay-client-secret");
        _vault.SetSecret("ebay/refresh-token", "test-ebay-refresh-token");
        _vault.SetSecret("ebay/marketplace-id", "EBAY_US");
    }

    [Fact]
    public void ChannelCode_IsEbayUs()
    {
        _adapter.ChannelCode.ShouldBe("EBAY_US");
    }

    [Fact]
    public async Task SubmitListing_ReturnsSuccess_WhenBothStepsSucceed()
    {
        // Arrange — OAuth token response
        EnqueueTokenResponse();

        // Create offer response
        _httpHandler.EnqueueResponse(HttpStatusCode.OK, new
        {
            offerId = "OFFER-ABC-123"
        });

        // Publish offer response
        _httpHandler.EnqueueResponse(HttpStatusCode.OK, new
        {
            listingId = "LISTING-XYZ-789"
        });

        var submission = CreateTestSubmission();

        // Act
        var result = await _adapter.SubmitListingAsync(submission);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.ExternalSubmissionId.ShouldNotBeNullOrEmpty();
        result.ExternalSubmissionId.ShouldStartWith("ebay-");
        result.ExternalSubmissionId.ShouldContain("OFFER-ABC-123");
    }

    [Fact]
    public async Task SubmitListing_ReturnsFailure_WhenCreateOfferFails()
    {
        // Arrange — OAuth token (successful auth)
        EnqueueTokenResponse();

        // Create offer returns 400 Bad Request
        _httpHandler.EnqueueResponse(HttpStatusCode.BadRequest,
            new { errors = new[] { new { errorId = 25001, message = "Invalid SKU format" } } });

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
    public async Task SubmitListing_ReturnsFailure_WhenPublishOfferFails()
    {
        // Arrange — OAuth token (successful auth)
        EnqueueTokenResponse();

        // Create offer succeeds
        _httpHandler.EnqueueResponse(HttpStatusCode.OK, new
        {
            offerId = "OFFER-CREATED-OK"
        });

        // Publish offer fails (e.g., listing policy violation)
        _httpHandler.EnqueueResponse(HttpStatusCode.BadRequest,
            new { errors = new[] { new { errorId = 25002, message = "Listing policy violation" } } });

        var submission = CreateTestSubmission();

        // Act
        var result = await _adapter.SubmitListingAsync(submission);

        // Assert
        result.IsSuccess.ShouldBeFalse();
        result.ExternalSubmissionId.ShouldBeNull();
        result.ErrorMessage.ShouldNotBeNullOrEmpty();
        result.ErrorMessage.ShouldContain("publish");
    }

    [Fact]
    public async Task SubmitListing_BuildsCorrectAuthHeader()
    {
        // Arrange — OAuth token response
        EnqueueTokenResponse();

        // Create and publish offer responses
        _httpHandler.EnqueueResponse(HttpStatusCode.OK, new { offerId = "OFFER-001" });
        _httpHandler.EnqueueResponse(HttpStatusCode.OK, new { listingId = "LIST-001" });

        var submission = CreateTestSubmission();

        // Act
        await _adapter.SubmitListingAsync(submission);

        // Assert — verify the token exchange request (first request)
        _httpHandler.SentRequests.Count.ShouldBe(3); // 1 token + 1 create + 1 publish

        var tokenRequest = _httpHandler.SentRequests[0];
        tokenRequest.RequestUri!.ToString().ShouldBe("https://api.ebay.com/identity/v1/oauth2/token");
        tokenRequest.Method.ShouldBe(HttpMethod.Post);

        // Verify Basic auth header is correctly base64-encoded
        tokenRequest.Headers.Authorization.ShouldNotBeNull();
        tokenRequest.Headers.Authorization!.Scheme.ShouldBe("Basic");

        var expectedCredentials = Convert.ToBase64String(
            Encoding.UTF8.GetBytes("test-ebay-client-id:test-ebay-client-secret"));
        tokenRequest.Headers.Authorization.Parameter.ShouldBe(expectedCredentials);

        // Verify create offer request has Bearer token and marketplace ID header
        var createRequest = _httpHandler.SentRequests[1];
        createRequest.Headers.Authorization.ShouldNotBeNull();
        createRequest.Headers.Authorization!.Scheme.ShouldBe("Bearer");
        createRequest.Headers.GetValues("X-EBAY-C-MARKETPLACE-ID").ShouldContain("EBAY_US");

        // Verify publish request
        var publishRequest = _httpHandler.SentRequests[2];
        publishRequest.RequestUri!.ToString().ShouldContain("/offer/OFFER-001/publish");
        publishRequest.Headers.Authorization.ShouldNotBeNull();
        publishRequest.Headers.Authorization!.Scheme.ShouldBe("Bearer");
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
            access_token = "cached-ebay-token",
            token_type = "Bearer",
            expires_in = 7200
        });

        // Two sets of create + publish responses (4 responses for 2 submissions)
        _httpHandler.EnqueueResponse(HttpStatusCode.OK, new { offerId = "OFFER-001" });
        _httpHandler.EnqueueResponse(HttpStatusCode.OK, new { listingId = "LIST-001" });
        _httpHandler.EnqueueResponse(HttpStatusCode.OK, new { offerId = "OFFER-002" });
        _httpHandler.EnqueueResponse(HttpStatusCode.OK, new { listingId = "LIST-002" });

        // Act — two submissions
        var result1 = await _adapter.SubmitListingAsync(CreateTestSubmission("SKU-001"));
        var result2 = await _adapter.SubmitListingAsync(CreateTestSubmission("SKU-002"));

        // Assert — only 1 token request + 2x (create + publish) = 5 total (not 6)
        result1.IsSuccess.ShouldBeTrue();
        result2.IsSuccess.ShouldBeTrue();
        _httpHandler.SentRequests.Count.ShouldBe(5);

        // First request is token exchange
        _httpHandler.SentRequests[0].RequestUri!.ToString().ShouldContain("api.ebay.com/identity");
        // Remaining 4 are create/publish pairs
        _httpHandler.SentRequests[1].RequestUri!.ToString().ShouldContain("/offer");
        _httpHandler.SentRequests[2].RequestUri!.ToString().ShouldContain("/publish");
        _httpHandler.SentRequests[3].RequestUri!.ToString().ShouldContain("/offer");
        _httpHandler.SentRequests[4].RequestUri!.ToString().ShouldContain("/publish");
    }

    [Fact]
    public async Task CheckSubmissionStatus_ReturnsPendingStatus()
    {
        // Act — skeleton implementation, should return not-live/not-failed
        var status = await _adapter.CheckSubmissionStatusAsync("ebay-OFFER-123");

        // Assert
        status.ExternalSubmissionId.ShouldBe("ebay-OFFER-123");
        status.IsLive.ShouldBeFalse();
        status.IsFailed.ShouldBeFalse();
    }

    [Fact]
    public async Task DeactivateListing_ReturnsFalse()
    {
        // Act — skeleton implementation
        var result = await _adapter.DeactivateListingAsync("ebay-listing-456");

        // Assert
        result.ShouldBeFalse();
    }

    private void EnqueueTokenResponse()
    {
        _httpHandler.EnqueueResponse(HttpStatusCode.OK, new
        {
            access_token = "test-ebay-access-token",
            token_type = "Bearer",
            expires_in = 7200
        });
    }

    private static ListingSubmission CreateTestSubmission(string sku = "SKU-001") =>
        new(
            ListingId: Guid.NewGuid(),
            Sku: sku,
            ChannelCode: "EBAY_US",
            ProductName: "Premium Critter Kibble",
            Description: "High-quality kibble for discerning critters",
            Category: "20744",
            Price: 29.99m);

    public void Dispose()
    {
        _httpHandler.Dispose();
    }
}
