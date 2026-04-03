using System.Net;
using Marketplaces.Adapters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging.Abstractions;
using Polly;
using static Marketplaces.Api.IntegrationTests.Helpers.MarketplaceAdapterTestHelpers;

namespace Marketplaces.Api.IntegrationTests;

/// <summary>
/// Verifies that Polly resilience pipelines registered in Marketplaces.Api/Program.cs
/// (P-8) behave correctly for all three real adapters:
///   - 429 TooManyRequests → retry → success
///   - 5xx InternalServerError → retry → success
///   - 401 Unauthorized → no retry (adapter handles auth failures, not Polly; see ADR 0056)
///
/// Tests build the adapter with the same Polly pipeline configuration as Program.cs,
/// using FakeHttpMessageHandler as the primary handler and a very short retry delay
/// to keep test execution fast.
/// </summary>
public sealed class AdapterResilienceTests : IDisposable
{
    private readonly FakeVaultClient _vault = new();

    public AdapterResilienceTests()
    {
        // Amazon credentials
        _vault.SetSecret("amazon/client-id", "test-client-id");
        _vault.SetSecret("amazon/client-secret", "test-client-secret");
        _vault.SetSecret("amazon/refresh-token", "test-refresh-token");
        _vault.SetSecret("amazon/seller-id", "A1B2C3SELLER");
        _vault.SetSecret("amazon/marketplace-id", "ATVPDKIKX0DER");

        // Walmart credentials
        _vault.SetSecret("walmart/client-id", "test-walmart-client-id");
        _vault.SetSecret("walmart/client-secret", "test-walmart-client-secret");
        _vault.SetSecret("walmart/seller-id", "WALMART-SELLER-123");

        // eBay credentials
        _vault.SetSecret("ebay/client-id", "test-ebay-client-id");
        _vault.SetSecret("ebay/client-secret", "test-ebay-client-secret");
        _vault.SetSecret("ebay/refresh-token", "test-ebay-refresh-token");
        _vault.SetSecret("ebay/marketplace-id", "EBAY_US");
    }

    // ─── Amazon ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Amazon_RetriesOnTooManyRequests_ThenSucceeds()
    {
        // Arrange
        var handler = new FakeHttpMessageHandler();
        var adapter = BuildAmazonAdapter(handler);

        // Token exchange succeeds first
        handler.EnqueueResponse(HttpStatusCode.OK, new { access_token = "token", expires_in = 3600 });
        // First SP-API attempt → 429; Polly retries
        handler.EnqueueResponse(HttpStatusCode.TooManyRequests);
        // Second attempt → success
        handler.EnqueueResponse(HttpStatusCode.OK, new { sku = "SKU-001", status = "ACCEPTED" });

        // Act
        var result = await adapter.SubmitListingAsync(CreateAmazonSubmission());

        // Assert — Polly retried the 429 and the second attempt succeeded
        result.IsSuccess.ShouldBeTrue();
        // 3 requests: 1 token + 1 retry'd 429 + 1 success
        handler.SentRequests.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Amazon_RetriesOnInternalServerError_ThenSucceeds()
    {
        // Arrange
        var handler = new FakeHttpMessageHandler();
        var adapter = BuildAmazonAdapter(handler);

        handler.EnqueueResponse(HttpStatusCode.OK, new { access_token = "token", expires_in = 3600 });
        handler.EnqueueResponse(HttpStatusCode.InternalServerError);
        handler.EnqueueResponse(HttpStatusCode.OK, new { sku = "SKU-001", status = "ACCEPTED" });

        // Act
        var result = await adapter.SubmitListingAsync(CreateAmazonSubmission());

        // Assert — Polly retried the 500 and the second attempt succeeded
        result.IsSuccess.ShouldBeTrue();
        handler.SentRequests.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Amazon_DoesNotRetryOn401()
    {
        // Arrange
        var handler = new FakeHttpMessageHandler();
        var adapter = BuildAmazonAdapter(handler);

        handler.EnqueueResponse(HttpStatusCode.OK, new { access_token = "token", expires_in = 3600 });
        // SP-API returns 401 (expired/invalid token at the API endpoint)
        handler.EnqueueResponse(HttpStatusCode.Unauthorized);

        // Act
        var result = await adapter.SubmitListingAsync(CreateAmazonSubmission());

        // Assert — failure, and exactly 2 requests (1 token + 1 API call; no Polly retry on 401)
        result.IsSuccess.ShouldBeFalse();
        handler.SentRequests.Count.ShouldBe(2);
    }

    // ─── Walmart ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Walmart_RetriesOnTooManyRequests_ThenSucceeds()
    {
        // Arrange
        var handler = new FakeHttpMessageHandler();
        var adapter = BuildWalmartAdapter(handler);

        handler.EnqueueResponse(HttpStatusCode.OK, new { access_token = "token", token_type = "Bearer", expires_in = 900 });
        handler.EnqueueResponse(HttpStatusCode.TooManyRequests);
        handler.EnqueueResponse(HttpStatusCode.OK, new { feedId = "FEED-RETRY-001" });

        // Act
        var result = await adapter.SubmitListingAsync(CreateWalmartSubmission());

        result.IsSuccess.ShouldBeTrue();
        handler.SentRequests.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Walmart_RetriesOnInternalServerError_ThenSucceeds()
    {
        // Arrange
        var handler = new FakeHttpMessageHandler();
        var adapter = BuildWalmartAdapter(handler);

        handler.EnqueueResponse(HttpStatusCode.OK, new { access_token = "token", token_type = "Bearer", expires_in = 900 });
        handler.EnqueueResponse(HttpStatusCode.InternalServerError);
        handler.EnqueueResponse(HttpStatusCode.OK, new { feedId = "FEED-RETRY-002" });

        // Act
        var result = await adapter.SubmitListingAsync(CreateWalmartSubmission());

        result.IsSuccess.ShouldBeTrue();
        handler.SentRequests.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Walmart_DoesNotRetryOn401()
    {
        // Arrange
        var handler = new FakeHttpMessageHandler();
        var adapter = BuildWalmartAdapter(handler);

        handler.EnqueueResponse(HttpStatusCode.OK, new { access_token = "token", token_type = "Bearer", expires_in = 900 });
        handler.EnqueueResponse(HttpStatusCode.Unauthorized);

        // Act
        var result = await adapter.SubmitListingAsync(CreateWalmartSubmission());

        result.IsSuccess.ShouldBeFalse();
        handler.SentRequests.Count.ShouldBe(2);
    }

    // ─── eBay ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Ebay_RetriesOnTooManyRequests_ThenSucceeds()
    {
        // Arrange — eBay has a two-step flow: token → create offer → publish offer
        var handler = new FakeHttpMessageHandler();
        var adapter = BuildEbayAdapter(handler);

        // Token exchange
        handler.EnqueueResponse(HttpStatusCode.OK, new { access_token = "token", token_type = "Bearer", expires_in = 7200 });
        // Create offer: first attempt 429, retry → 200
        handler.EnqueueResponse(HttpStatusCode.TooManyRequests);
        handler.EnqueueResponse(HttpStatusCode.OK, new { offerId = "OFFER-RETRY-001" });
        // Publish offer: 200
        handler.EnqueueResponse(HttpStatusCode.OK, new { listingId = "LISTING-RETRY-001" });

        // Act
        var result = await adapter.SubmitListingAsync(CreateEbaySubmission());

        result.IsSuccess.ShouldBeTrue();
        // 4 requests: 1 token + 1 429 create + 1 success create + 1 publish
        handler.SentRequests.Count.ShouldBe(4);
    }

    [Fact]
    public async Task Ebay_RetriesOnInternalServerError_ThenSucceeds()
    {
        // Arrange
        var handler = new FakeHttpMessageHandler();
        var adapter = BuildEbayAdapter(handler);

        handler.EnqueueResponse(HttpStatusCode.OK, new { access_token = "token", token_type = "Bearer", expires_in = 7200 });
        handler.EnqueueResponse(HttpStatusCode.InternalServerError);
        handler.EnqueueResponse(HttpStatusCode.OK, new { offerId = "OFFER-RETRY-002" });
        handler.EnqueueResponse(HttpStatusCode.OK, new { listingId = "LISTING-RETRY-002" });

        // Act
        var result = await adapter.SubmitListingAsync(CreateEbaySubmission());

        result.IsSuccess.ShouldBeTrue();
        handler.SentRequests.Count.ShouldBe(4);
    }

    [Fact]
    public async Task Ebay_DoesNotRetryOn401()
    {
        // Arrange
        var handler = new FakeHttpMessageHandler();
        var adapter = BuildEbayAdapter(handler);

        handler.EnqueueResponse(HttpStatusCode.OK, new { access_token = "token", token_type = "Bearer", expires_in = 7200 });
        handler.EnqueueResponse(HttpStatusCode.Unauthorized);

        // Act
        var result = await adapter.SubmitListingAsync(CreateEbaySubmission());

        result.IsSuccess.ShouldBeFalse();
        // 2 requests: 1 token + 1 API call (no Polly retry on 401)
        handler.SentRequests.Count.ShouldBe(2);
    }

    // ─── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds an adapter backed by a real Polly pipeline (same strategy as Program.cs)
    /// with a minimal retry delay for test performance.
    /// </summary>
    private static IHttpClientFactory BuildResilienceFactory(string clientName, FakeHttpMessageHandler handler)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient(clientName)
            .ConfigurePrimaryHttpMessageHandler(() => handler)
            .AddResilienceHandler($"{clientName}-test-resilience", pipeline =>
            {
                pipeline.AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = 3,
                    BackoffType = DelayBackoffType.Constant,
                    Delay = TimeSpan.FromMilliseconds(1) // Fast for tests; production uses 1s exponential
                    // ShouldHandle defaults: 408, 429, 500+, HttpRequestException — excludes 401
                });
            });
        return services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();
    }

    private AmazonMarketplaceAdapter BuildAmazonAdapter(FakeHttpMessageHandler handler)
    {
        var factory = BuildResilienceFactory("AmazonSpApi", handler);
        return new AmazonMarketplaceAdapter(_vault, factory, NullLogger<AmazonMarketplaceAdapter>.Instance);
    }

    private WalmartMarketplaceAdapter BuildWalmartAdapter(FakeHttpMessageHandler handler)
    {
        var factory = BuildResilienceFactory("WalmartApi", handler);
        return new WalmartMarketplaceAdapter(_vault, factory, NullLogger<WalmartMarketplaceAdapter>.Instance);
    }

    private EbayMarketplaceAdapter BuildEbayAdapter(FakeHttpMessageHandler handler)
    {
        var factory = BuildResilienceFactory("EbayApi", handler);
        return new EbayMarketplaceAdapter(_vault, factory, NullLogger<EbayMarketplaceAdapter>.Instance);
    }

    private static ListingSubmission CreateAmazonSubmission(string sku = "SKU-RESILIENCE") =>
        new(Guid.NewGuid(), sku, "AMAZON_US", "Test Product", "Test description", "Pet Food", 19.99m);

    private static ListingSubmission CreateWalmartSubmission(string sku = "SKU-RESILIENCE") =>
        new(Guid.NewGuid(), sku, "WALMART_US", "Test Product", "Test description", "Pet Food", 19.99m);

    private static ListingSubmission CreateEbaySubmission(string sku = "SKU-RESILIENCE") =>
        new(Guid.NewGuid(), sku, "EBAY_US", "Test Product", "Test description", "Pet Food", 19.99m);

    public void Dispose() { }
}
