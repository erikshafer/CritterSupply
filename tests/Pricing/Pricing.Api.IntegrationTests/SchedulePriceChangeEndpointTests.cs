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
        var effectiveDate = DateTimeOffset.UtcNow.AddDays(7);
        var result = await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new
            {
                newPrice = 19.99m,
                effectiveDate = effectiveDate,
                floorPrice = (decimal?)null,
                ceilingPrice = (decimal?)null
            }).ToUrl($"/api/pricing/products/{sku}/price/schedule");
            x.StatusCodeShouldBeOk();
        });

        // Assert: Verify schedule was created
        var response = result.ReadAsJson<dynamic>();
        response.ShouldNotBeNull();

        // Verify current price unchanged
        await using var verifySession = _fixture.GetDocumentSession();
        var priceView = await verifySession.LoadAsync<CurrentPriceView>(sku);
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

        // Act: Try to schedule price change in the past
        var pastDate = DateTimeOffset.UtcNow.AddDays(-1);
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new
            {
                newPrice = 14.99m,
                effectiveDate = pastDate,
                floorPrice = (decimal?)null,
                ceilingPrice = (decimal?)null
            }).ToUrl($"/api/pricing/products/{sku}/price/schedule");
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

        // Act: Try to schedule price below floor
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new
            {
                newPrice = 95.00m,  // Below floor
                effectiveDate = DateTimeOffset.UtcNow.AddDays(7),
                floorPrice = 100.00m,
                ceilingPrice = (decimal?)null
            }).ToUrl($"/api/pricing/products/{sku}/price/schedule");
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
                newPrice = 49.99m,
                effectiveDate = DateTimeOffset.UtcNow.AddDays(7),
                floorPrice = (decimal?)null,
                ceilingPrice = (decimal?)null
            }).ToUrl("/api/pricing/products/DOES-NOT-EXIST/price/schedule");
            x.StatusCodeShouldBe(404);
        });
    }
}
