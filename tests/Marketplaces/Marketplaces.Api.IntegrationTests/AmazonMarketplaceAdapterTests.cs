using System.Net;
using System.Text.Json;
using Marketplaces.Adapters;
using Marketplaces.Credentials;
using Microsoft.Extensions.Logging.Abstractions;

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
    public async Task CheckSubmissionStatus_ReturnsPendingStatus()
    {
        // Act — skeleton implementation, should return not-live/not-failed
        var status = await _adapter.CheckSubmissionStatusAsync("amzn-sub-123");

        // Assert
        status.ExternalSubmissionId.ShouldBe("amzn-sub-123");
        status.IsLive.ShouldBeFalse();
        status.IsFailed.ShouldBeFalse();
    }

    [Fact]
    public async Task DeactivateListing_ReturnsFalse()
    {
        // Act — skeleton implementation
        var result = await _adapter.DeactivateListingAsync("amzn-listing-456");

        // Assert
        result.ShouldBeFalse();
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

    // --- Test doubles ---

    /// <summary>
    /// Fake HTTP handler that queues responses and records sent requests.
    /// Reusable by Walmart and eBay adapter tests in M38.x.
    /// </summary>
    internal sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new();
        public List<HttpRequestMessage> SentRequests { get; } = [];

        public void EnqueueResponse(HttpStatusCode statusCode, object? body = null)
        {
            var response = new HttpResponseMessage(statusCode);
            if (body is not null)
            {
                response.Content = new StringContent(
                    JsonSerializer.Serialize(body),
                    System.Text.Encoding.UTF8,
                    "application/json");
            }
            _responses.Enqueue(response);
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            SentRequests.Add(request);

            if (_responses.Count == 0)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("No more queued responses in FakeHttpMessageHandler")
                });
            }

            return Task.FromResult(_responses.Dequeue());
        }
    }

    /// <summary>
    /// Fake vault client for adapter tests — returns pre-configured secrets.
    /// </summary>
    internal sealed class FakeVaultClient : IVaultClient
    {
        private readonly Dictionary<string, string> _secrets = new(StringComparer.OrdinalIgnoreCase);

        public void SetSecret(string path, string value) => _secrets[path] = value;

        public Task<string> GetSecretAsync(string path, CancellationToken ct = default)
        {
            if (!_secrets.TryGetValue(path, out var value))
                throw new InvalidOperationException($"Test vault: secret not found: {path}");
            return Task.FromResult(value);
        }
    }

    /// <summary>
    /// Fake IHttpClientFactory that returns a pre-configured HttpClient.
    /// </summary>
    internal sealed class FakeHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}
