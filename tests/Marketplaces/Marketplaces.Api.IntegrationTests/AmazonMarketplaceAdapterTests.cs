using System.Net;
using System.Text.Json;
using Marketplaces.Adapters;
using Marketplaces.Credentials;
using Microsoft.Extensions.Logging.Abstractions;
using static Marketplaces.Api.IntegrationTests.Helpers.MarketplaceAdapterTestHelpers;

namespace Marketplaces.Api.IntegrationTests;

/// <summary>
/// Tests for <see cref="AmazonMarketplaceAdapter"/> using a fake HTTP handler.
/// No real SP-API calls are made — the handler intercepts all HTTP traffic.
/// </summary>
public sealed class AmazonMarketplaceAdapterTests : IDisposable
{
    private readonly FakeHttpMessageHandler _httpHandler = new();
    private readonly FakeVaultClient _vault = new();
    private readonly AmazonMarketplaceAdapter _adapter;

    public AmazonMarketplaceAdapterTests()
    {
        var httpClient = new HttpClient(_httpHandler);
        var factory = new FakeHttpClientFactory(httpClient);
        _adapter = new AmazonMarketplaceAdapter(
            _vault,
            factory,
            NullLogger<AmazonMarketplaceAdapter>.Instance);

        // Default vault secrets for all tests
        _vault.SetSecret("amazon/client-id", "test-client-id");
        _vault.SetSecret("amazon/client-secret", "test-client-secret");
        _vault.SetSecret("amazon/refresh-token", "test-refresh-token");
        _vault.SetSecret("amazon/seller-id", "A1B2C3SELLER");
        _vault.SetSecret("amazon/marketplace-id", "ATVPDKIKX0DER");
    }

    [Fact]
    public void ChannelCode_IsAmazonUs()
    {
        _adapter.ChannelCode.ShouldBe("AMAZON_US");
    }

    [Fact]
    public async Task SubmitListing_ReturnsSuccess_WhenSpApiReturns2xx()
    {
        // Arrange — LWA token response
        _httpHandler.EnqueueResponse(HttpStatusCode.OK, new
        {
            access_token = "test-access-token",
            token_type = "bearer",
            expires_in = 3600
        });

        // SP-API listings response
        _httpHandler.EnqueueResponse(HttpStatusCode.OK, new
        {
            sku = "SKU-001",
            status = "ACCEPTED",
            submissionId = "sub-12345"
        });

        var submission = CreateTestSubmission();

        // Act
        var result = await _adapter.SubmitListingAsync(submission);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.ExternalSubmissionId.ShouldNotBeNullOrEmpty();
        result.ExternalSubmissionId.ShouldStartWith("amzn-");
    }

    [Fact]
    public async Task SubmitListing_ReturnsFailure_WhenSpApiReturns4xx()
    {
        // Arrange — LWA token response (successful auth)
        _httpHandler.EnqueueResponse(HttpStatusCode.OK, new
        {
            access_token = "test-access-token",
            token_type = "bearer",
            expires_in = 3600
        });

        // SP-API returns 400 Bad Request
        _httpHandler.EnqueueResponse(HttpStatusCode.BadRequest,
            new { errors = new[] { new { code = "INVALID_INPUT", message = "Invalid SKU format" } } });

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
        // Arrange — LWA token response
        _httpHandler.EnqueueResponse(HttpStatusCode.OK, new
        {
            access_token = "test-access-token",
            token_type = "bearer",
            expires_in = 3600
        });

        // SP-API success
        _httpHandler.EnqueueResponse(HttpStatusCode.OK, new
        {
            sku = "SKU-001",
            status = "ACCEPTED"
        });

        var submission = CreateTestSubmission();

        // Act
        await _adapter.SubmitListingAsync(submission);

        // Assert — verify the SP-API request (second request; first is LWA token)
        _httpHandler.SentRequests.Count.ShouldBe(2);

        var tokenRequest = _httpHandler.SentRequests[0];
        tokenRequest.RequestUri!.ToString().ShouldBe("https://api.amazon.com/auth/o2/token");
        tokenRequest.Method.ShouldBe(HttpMethod.Post);

        var listingRequest = _httpHandler.SentRequests[1];
        listingRequest.Method.ShouldBe(HttpMethod.Put);
        listingRequest.RequestUri!.ToString().ShouldContain("/listings/2021-08-01/items/A1B2C3SELLER/SKU-001");
        listingRequest.Headers.GetValues("x-amz-access-token").ShouldContain("test-access-token");
    }

    [Fact]
    public async Task SubmitListing_ReturnsFailure_WhenLwaTokenFails()
    {
        // Arrange — LWA returns 401
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
    public async Task SubmitListing_CachesLwaToken_AcrossMultipleCalls()
    {
        // Arrange — only one LWA response (should be cached for second call)
        _httpHandler.EnqueueResponse(HttpStatusCode.OK, new
        {
            access_token = "cached-token",
            token_type = "bearer",
            expires_in = 3600
        });

        // Two SP-API responses
        _httpHandler.EnqueueResponse(HttpStatusCode.OK, new { sku = "SKU-001", status = "ACCEPTED" });
        _httpHandler.EnqueueResponse(HttpStatusCode.OK, new { sku = "SKU-002", status = "ACCEPTED" });

        // Act — two submissions
        var result1 = await _adapter.SubmitListingAsync(CreateTestSubmission("SKU-001"));
        var result2 = await _adapter.SubmitListingAsync(CreateTestSubmission("SKU-002"));

        // Assert — only 1 LWA request + 2 SP-API requests = 3 total (not 4)
        result1.IsSuccess.ShouldBeTrue();
        result2.IsSuccess.ShouldBeTrue();
        _httpHandler.SentRequests.Count.ShouldBe(3);

        // First request is LWA token, second and third are SP-API
        _httpHandler.SentRequests[0].RequestUri!.ToString().ShouldContain("api.amazon.com/auth");
        _httpHandler.SentRequests[1].RequestUri!.ToString().ShouldContain("/listings/");
        _httpHandler.SentRequests[2].RequestUri!.ToString().ShouldContain("/listings/");
    }

    [Fact]
    public async Task CheckSubmissionStatus_ReturnsLive_WhenListingIsBuyable()
    {
        // Arrange — LWA token response
        _httpHandler.EnqueueResponse(HttpStatusCode.OK, new
        {
            access_token = "test-access-token",
            token_type = "bearer",
            expires_in = 3600
        });

        // SP-API GET listing status — BUYABLE means live on marketplace
        _httpHandler.EnqueueResponse(HttpStatusCode.OK, new
        {
            summaries = new[] { new { status = "BUYABLE" } }
        });

        // Act
        var status = await _adapter.CheckSubmissionStatusAsync("amzn-SKU-LIVE");

        // Assert
        status.ExternalSubmissionId.ShouldBe("amzn-SKU-LIVE");
        status.IsLive.ShouldBeTrue();
        status.IsFailed.ShouldBeFalse();

        // Verify request URL includes includedData=summaries and correct auth headers
        var getRequest = _httpHandler.SentRequests[1];
        getRequest.Method.ShouldBe(HttpMethod.Get);
        getRequest.RequestUri!.ToString().ShouldContain("/listings/2021-08-01/items/A1B2C3SELLER/SKU-LIVE");
        getRequest.RequestUri!.ToString().ShouldContain("includedData=summaries");
        getRequest.Headers.Authorization!.Scheme.ShouldBe("Bearer");
        getRequest.Headers.GetValues("x-amz-access-token").ShouldContain("test-access-token");
    }

    [Fact]
    public async Task CheckSubmissionStatus_ReturnsFailed_WhenListingIsInactive()
    {
        // Arrange — LWA token response
        _httpHandler.EnqueueResponse(HttpStatusCode.OK, new
        {
            access_token = "test-access-token",
            token_type = "bearer",
            expires_in = 3600
        });

        // SP-API GET listing status — INACTIVE is a non-BUYABLE state
        _httpHandler.EnqueueResponse(HttpStatusCode.OK, new
        {
            summaries = new[] { new { status = "INACTIVE" } }
        });

        // Act
        var status = await _adapter.CheckSubmissionStatusAsync("amzn-SKU-INACTIVE");

        // Assert
        status.ExternalSubmissionId.ShouldBe("amzn-SKU-INACTIVE");
        status.IsLive.ShouldBeFalse();
        status.IsFailed.ShouldBeTrue();
        status.FailureReason.ShouldContain("INACTIVE");
    }

    [Fact]
    public async Task CheckSubmissionStatus_ReturnsPending_WhenListingNotFound()
    {
        // Arrange — LWA token response
        _httpHandler.EnqueueResponse(HttpStatusCode.OK, new
        {
            access_token = "test-access-token",
            token_type = "bearer",
            expires_in = 3600
        });

        // SP-API returns 404 — listing not yet visible on marketplace; treat as pending, not failed
        _httpHandler.EnqueueResponse(HttpStatusCode.NotFound, new
        {
            errors = new[] { new { code = "NOT_FOUND", message = "Listings item not found" } }
        });

        // Act
        var status = await _adapter.CheckSubmissionStatusAsync("amzn-SKU-PENDING");

        // Assert — guard rail: 404 is pending, not failed
        status.ExternalSubmissionId.ShouldBe("amzn-SKU-PENDING");
        status.IsLive.ShouldBeFalse();
        status.IsFailed.ShouldBeFalse();
    }

    [Fact]
    public async Task DeactivateListing_ReturnsTrue_WhenApiSucceeds()
    {
        // Arrange — LWA token response
        _httpHandler.EnqueueResponse(HttpStatusCode.OK, new
        {
            access_token = "test-access-token",
            token_type = "bearer",
            expires_in = 3600
        });

        // SP-API PATCH returns 200 (deactivation succeeded)
        _httpHandler.EnqueueResponse(HttpStatusCode.OK, new
        {
            sku = "SKU-DEACTIVATE",
            status = "ACCEPTED"
        });

        // Act
        var result = await _adapter.DeactivateListingAsync("amzn-SKU-DEACTIVATE");

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public async Task DeactivateListing_ReturnsFalse_WhenApiFails()
    {
        // Arrange — LWA token response
        _httpHandler.EnqueueResponse(HttpStatusCode.OK, new
        {
            access_token = "test-access-token",
            token_type = "bearer",
            expires_in = 3600
        });

        // SP-API PATCH returns 404 (SKU not found)
        _httpHandler.EnqueueResponse(HttpStatusCode.NotFound,
            new { errors = new[] { new { code = "NOT_FOUND", message = "SKU does not exist" } } });

        // Act
        var result = await _adapter.DeactivateListingAsync("amzn-SKU-MISSING");

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public async Task DeactivateListing_BuildsCorrectRequest()
    {
        // Arrange — LWA token response
        _httpHandler.EnqueueResponse(HttpStatusCode.OK, new
        {
            access_token = "test-deactivate-token",
            token_type = "bearer",
            expires_in = 3600
        });

        // PATCH succeeds
        _httpHandler.EnqueueResponse(HttpStatusCode.OK, new { sku = "SKU-REQ-CHECK", status = "ACCEPTED" });

        // Act
        await _adapter.DeactivateListingAsync("amzn-SKU-REQ-CHECK");

        // Assert — second request is the PATCH deactivation call
        _httpHandler.SentRequests.Count.ShouldBe(2);
        var deactivateRequest = _httpHandler.SentRequests[1];

        // Correct HTTP method and URL shape
        deactivateRequest.Method.ShouldBe(new HttpMethod("PATCH"));
        deactivateRequest.RequestUri!.ToString().ShouldContain("/listings/2021-08-01/items/");
        deactivateRequest.RequestUri!.ToString().ShouldContain("SKU-REQ-CHECK");
        deactivateRequest.RequestUri!.ToString().ShouldContain("marketplaceIds=");
        // amzn- prefix stripped correctly (not in URL)
        deactivateRequest.RequestUri!.ToString().ShouldNotContain("amzn-");

        // Required auth headers
        deactivateRequest.Headers.Authorization!.Scheme.ShouldBe("Bearer");
        deactivateRequest.Headers.Authorization!.Parameter.ShouldBe("test-deactivate-token");
        deactivateRequest.Headers.TryGetValues("x-amz-access-token", out var xAmzTokens).ShouldBeTrue();
        xAmzTokens!.First().ShouldBe("test-deactivate-token");
    }

    private static ListingSubmission CreateTestSubmission(string sku = "SKU-001") =>
        new(
            ListingId: Guid.NewGuid(),
            Sku: sku,
            ChannelCode: "AMAZON_US",
            ProductName: "Premium Critter Kibble",
            Description: "High-quality kibble for discerning critters",
            Category: "Pet Food",
            Price: 29.99m);

    public void Dispose()
    {
        _httpHandler.Dispose();
    }
}
