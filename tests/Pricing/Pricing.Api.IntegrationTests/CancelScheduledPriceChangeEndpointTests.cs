using Marten;
using Pricing.Products;
using Shouldly;

namespace Pricing.Api.IntegrationTests;

/// <summary>
/// Integration tests for DELETE /api/pricing/products/{sku}/price/schedule/{scheduleId} endpoint.
/// Tests the CancelScheduledPriceChange write operation added in M32.1 Session 2.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class CancelScheduledPriceChangeEndpointTests(TestFixture fixture) : IAsyncLifetime
{
    private readonly TestFixture _fixture = fixture;

    public Task InitializeAsync() => _fixture.CleanAllDataAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task CancelScheduledPriceChange_PendingSchedule_CancelsSuccessfully()
    {
        // Arrange: Product with scheduled price change
        await using var session = _fixture.GetDocumentSession();
        var sku = "BIRD-FEEDER";
        var streamId = ProductPrice.StreamId(sku);
        var scheduleId = Guid.NewGuid();

        session.Events.StartStream<ProductPrice>(
            streamId,
            new ProductRegistered(streamId, sku, DateTimeOffset.UtcNow.AddDays(-1)),
            new InitialPriceSet(
                streamId,
                sku,
                Money.Of(34.99m, "USD"),
                null,
                null,
                Guid.NewGuid(),
                DateTimeOffset.UtcNow.AddDays(-1)),
            new PriceChangeScheduled(
                streamId,
                sku,
                scheduleId,
                Money.Of(29.99m, "USD"),
                DateTimeOffset.UtcNow.AddDays(7),
                Guid.NewGuid(),
                DateTimeOffset.UtcNow));

        await session.SaveChangesAsync();

        // Act: Cancel the scheduled price change
        var result = await _fixture.Host.Scenario(x =>
        {
            x.Delete.Url($"/api/pricing/products/{sku}/price/schedule/{scheduleId}");
            x.StatusCodeShouldBeOk();
        });

        // Assert: Verify response
        var response = result.ReadAsJson<dynamic>();
        response.ShouldNotBeNull();
    }

    [Fact]
    public async Task CancelScheduledPriceChange_NonExistentSchedule_ReturnsNotFound()
    {
        // Arrange: Product with no scheduled price
        await using var session = _fixture.GetDocumentSession();
        var sku = "GUINEA-PIG-FOOD";
        var streamId = ProductPrice.StreamId(sku);

        session.Events.StartStream<ProductPrice>(
            streamId,
            new ProductRegistered(streamId, sku, DateTimeOffset.UtcNow.AddDays(-1)),
            new InitialPriceSet(
                streamId,
                sku,
                Money.Of(12.99m, "USD"),
                null,
                null,
                Guid.NewGuid(),
                DateTimeOffset.UtcNow.AddDays(-1)));

        await session.SaveChangesAsync();

        // Act: Try to cancel non-existent schedule
        var nonExistentScheduleId = Guid.NewGuid();
        await _fixture.Host.Scenario(x =>
        {
            x.Delete.Url($"/api/pricing/products/{sku}/price/schedule/{nonExistentScheduleId}");
            x.StatusCodeShouldBe(404);
        });
    }

    [Fact]
    public async Task CancelScheduledPriceChange_NonExistentProduct_ReturnsNotFound()
    {
        // Act: Try to cancel schedule for non-existent product
        var scheduleId = Guid.NewGuid();
        await _fixture.Host.Scenario(x =>
        {
            x.Delete.Url($"/api/pricing/products/DOES-NOT-EXIST/price/schedule/{scheduleId}");
            x.StatusCodeShouldBe(404);
        });
    }

    [Fact]
    public async Task CancelScheduledPriceChange_AlreadyCancelled_ReturnsError()
    {
        // Arrange: Product with already-cancelled schedule
        await using var session = _fixture.GetDocumentSession();
        var sku = "AQUARIUM-FILTER";
        var streamId = ProductPrice.StreamId(sku);
        var scheduleId = Guid.NewGuid();

        session.Events.StartStream<ProductPrice>(
            streamId,
            new ProductRegistered(streamId, sku, DateTimeOffset.UtcNow.AddDays(-2)),
            new InitialPriceSet(
                streamId,
                sku,
                Money.Of(59.99m, "USD"),
                null,
                null,
                Guid.NewGuid(),
                DateTimeOffset.UtcNow.AddDays(-2)),
            new PriceChangeScheduled(
                streamId,
                sku,
                scheduleId,
                Money.Of(49.99m, "USD"),
                DateTimeOffset.UtcNow.AddDays(7),
                Guid.NewGuid(),
                DateTimeOffset.UtcNow.AddDays(-1)),
            new ScheduledPriceChangeCancelled(
                streamId,
                sku,
                scheduleId,
                null,
                Guid.NewGuid(),
                DateTimeOffset.UtcNow.AddHours(-1)));

        await session.SaveChangesAsync();

        // Act: Try to cancel again
        await _fixture.Host.Scenario(x =>
        {
            x.Delete.Url($"/api/pricing/products/{sku}/price/schedule/{scheduleId}");
            x.StatusCodeShouldBe(400);
        });
    }
}
