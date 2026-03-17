using Marten;
using Pricing.Products;
using Shouldly;

namespace Pricing.Api.IntegrationTests;

/// <summary>
/// Integration tests for POST /api/pricing/products/{sku}/price/schedule endpoint.
/// Tests the SchedulePriceChange write operation added in M32.1 Session 2.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class SchedulePriceChangeEndpointTests(TestFixture fixture) : IAsyncLifetime
{
    private readonly TestFixture _fixture = fixture;

    public Task InitializeAsync() => _fixture.CleanAllDataAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task SchedulePriceChange_PublishedProduct_SchedulesFuturePrice()
    {
        // Arrange: Product with current price
        await using var session = _fixture.GetDocumentSession();
        var sku = "DOG-COLLAR-MEDIUM";
        var streamId = ProductPrice.StreamId(sku);

        session.Events.StartStream<ProductPrice>(
            streamId,
            new ProductRegistered(streamId, sku, DateTimeOffset.UtcNow.AddDays(-1)),
            new InitialPriceSet(
                streamId,
                sku,
                Money.Of(24.99m, "USD"),
                null,
                null,
                Guid.NewGuid(),
                DateTimeOffset.UtcNow.AddDays(-1)));

        await session.SaveChangesAsync();

        // Act: Schedule price change for future
        var scheduledFor = DateTimeOffset.UtcNow.AddDays(7);
        var result = await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new
            {
                newAmount = 19.99m,
                currency = "USD",
                scheduledFor = scheduledFor
            }).ToUrl($"/api/pricing/products/{sku}/schedule");
            x.StatusCodeShouldBeOk();
        });

        // Verify current price unchanged
        await using var verifySession = _fixture.GetDocumentSession();
        var priceView = await verifySession.LoadAsync<CurrentPriceView>(sku.ToUpperInvariant());
        priceView.ShouldNotBeNull();
        priceView.BasePrice.ShouldBe(24.99m);  // Still old price
    }

    [Fact]
    public async Task SchedulePriceChange_PastDate_ReturnsValidationError()
    {
        // Arrange: Product with price
        await using var session = _fixture.GetDocumentSession();
        var sku = "CAT-LITTER-20LB";
        var streamId = ProductPrice.StreamId(sku);

        session.Events.StartStream<ProductPrice>(
            streamId,
            new ProductRegistered(streamId, sku, DateTimeOffset.UtcNow.AddDays(-1)),
            new InitialPriceSet(
                streamId,
                sku,
                Money.Of(15.99m, "USD"),
                null,
                null,
                Guid.NewGuid(),
                DateTimeOffset.UtcNow.AddDays(-1)));

        await session.SaveChangesAsync();

        // Act: Try to schedule price change in the past (FluentValidation will reject)
        var pastDate = DateTimeOffset.UtcNow.AddDays(-1);
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new
            {
                newAmount = 14.99m,
                currency = "USD",
                scheduledFor = pastDate
            }).ToUrl($"/api/pricing/products/{sku}/schedule");
            x.StatusCodeShouldBe(400);
        });
    }

    [Fact]
    public async Task SchedulePriceChange_BelowFloorPrice_ReturnsValidationError()
    {
        // Arrange: Product with floor price
        await using var session = _fixture.GetDocumentSession();
        var sku = "RABBIT-HUTCH";
        var streamId = ProductPrice.StreamId(sku);

        session.Events.StartStream<ProductPrice>(
            streamId,
            new ProductRegistered(streamId, sku, DateTimeOffset.UtcNow.AddDays(-1)),
            new InitialPriceSet(
                streamId,
                sku,
                Money.Of(129.99m, "USD"),
                Money.Of(100.00m, "USD"),  // Floor price
                null,
                Guid.NewGuid(),
                DateTimeOffset.UtcNow.AddDays(-1)));

        await session.SaveChangesAsync();

        // Act: Try to schedule price below floor (endpoint will enforce existing floor constraint)
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new
            {
                newAmount = 95.00m,  // Below floor
                currency = "USD",
                scheduledFor = DateTimeOffset.UtcNow.AddDays(7)
            }).ToUrl($"/api/pricing/products/{sku}/schedule");
            x.StatusCodeShouldBe(400);
        });
    }

    [Fact]
    public async Task SchedulePriceChange_NonExistentProduct_ReturnsNotFound()
    {
        // Act: Try to schedule price for non-existent product
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new
            {
                newAmount = 49.99m,
                currency = "USD",
                scheduledFor = DateTimeOffset.UtcNow.AddDays(7)
            }).ToUrl("/api/pricing/products/DOES-NOT-EXIST/schedule");
            x.StatusCodeShouldBe(404);
        });
    }
}
