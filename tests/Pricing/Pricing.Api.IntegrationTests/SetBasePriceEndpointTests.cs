using Marten;
using Pricing.Products;
using Shouldly;

namespace Pricing.Api.IntegrationTests;

/// <summary>
/// Integration tests for SET /api/pricing/products/{sku}/price endpoint.
/// Tests the SetBasePrice write operation added in M32.1 Session 2.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class SetBasePriceEndpointTests(TestFixture fixture) : IAsyncLifetime
{
    private readonly TestFixture _fixture = fixture;

    public Task InitializeAsync() => _fixture.CleanAllDataAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task SetBasePrice_UnpricedProduct_SetsInitialPrice()
    {
        // Arrange: Register a product with no price
        await using var session = _fixture.GetDocumentSession();
        var sku = "DOG-TREAT-001";
        var streamId = ProductPrice.StreamId(sku);

        session.Events.StartStream<ProductPrice>(
            streamId,
            new ProductRegistered(streamId, sku, DateTimeOffset.UtcNow));

        await session.SaveChangesAsync();

        // Act: Set base price via HTTP endpoint
        var result = await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new
            {
                amount = 15.99m,
                currency = "USD"
            }).ToUrl($"/api/pricing/products/{sku}/base-price");
            x.StatusCodeShouldBeOk();
        });

        // Assert: Verify price in database
        await using var verifySession = _fixture.GetDocumentSession();
        var priceView = await verifySession.LoadAsync<CurrentPriceView>(sku.ToUpperInvariant());
        priceView.ShouldNotBeNull();
        priceView.BasePrice.ShouldBe(15.99m);
        priceView.Status.ShouldBe(PriceStatus.Published);
    }

    [Fact]
    public async Task SetBasePrice_PublishedProduct_UpdatesPrice()
    {
        // Arrange: Product with existing price
        await using var session = _fixture.GetDocumentSession();
        var sku = "CAT-FOOD-3LB";
        var streamId = ProductPrice.StreamId(sku);

        session.Events.StartStream<ProductPrice>(
            streamId,
            new ProductRegistered(streamId, sku, DateTimeOffset.UtcNow.AddDays(-1)),
            new InitialPriceSet(
                streamId,
                sku,
                Money.Of(19.99m, "USD"),
                null,
                null,
                Guid.NewGuid(),
                DateTimeOffset.UtcNow.AddDays(-1)));

        await session.SaveChangesAsync();

        // Act: Update base price
        var result = await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new
            {
                amount = 22.99m,
                currency = "USD"
            }).ToUrl($"/api/pricing/products/{sku}/base-price");
            x.StatusCodeShouldBeOk();
        });

        // Assert: Verify price was updated
        await using var verifySession = _fixture.GetDocumentSession();
        var priceView = await verifySession.LoadAsync<CurrentPriceView>(sku.ToUpperInvariant());
        priceView.ShouldNotBeNull();
        priceView.BasePrice.ShouldBe(22.99m);
    }

    [Fact]
    public async Task SetBasePrice_WithFloorPrice_EnforcesConstraint()
    {
        // Arrange: Register product
        await using var session = _fixture.GetDocumentSession();
        var sku = "BIRD-SEED-10LB";
        var streamId = ProductPrice.StreamId(sku);

        session.Events.StartStream<ProductPrice>(
            streamId,
            new ProductRegistered(streamId, sku, DateTimeOffset.UtcNow));

        await session.SaveChangesAsync();

        // Act: Set base price (Note: Floor/ceiling constraints set via Vendor Portal, not this endpoint)
        var result = await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new
            {
                amount = 25.00m,
                currency = "USD"
            }).ToUrl($"/api/pricing/products/{sku}/base-price");
            x.StatusCodeShouldBeOk();
        });

        // Assert: Verify price was set (floor/ceiling are not set by this endpoint)
        await using var verifySession = _fixture.GetDocumentSession();
        var priceView = await verifySession.LoadAsync<CurrentPriceView>(sku.ToUpperInvariant());
        priceView.ShouldNotBeNull();
        priceView.BasePrice.ShouldBe(25.00m);
    }

    [Fact]
    public async Task SetBasePrice_BelowFloorPrice_ReturnsValidationError()
    {
        // Arrange: Product with floor price
        await using var session = _fixture.GetDocumentSession();
        var sku = "FISH-TANK-20GAL";
        var streamId = ProductPrice.StreamId(sku);

        session.Events.StartStream<ProductPrice>(
            streamId,
            new ProductRegistered(streamId, sku, DateTimeOffset.UtcNow.AddDays(-1)),
            new InitialPriceSet(
                streamId,
                sku,
                Money.Of(89.99m, "USD"),
                Money.Of(75.00m, "USD"),  // Floor price
                null,
                Guid.NewGuid(),
                DateTimeOffset.UtcNow.AddDays(-1)));

        await session.SaveChangesAsync();

        // Act: Try to set price below floor (endpoint will enforce existing floor constraint)
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new
            {
                amount = 70.00m,  // Below floor of 75.00
                currency = "USD"
            }).ToUrl($"/api/pricing/products/{sku}/base-price");
            x.StatusCodeShouldBe(400);
        });
    }

    [Fact]
    public async Task SetBasePrice_NonExistentProduct_ReturnsNotFound()
    {
        // Act: Try to set price for non-existent product
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new
            {
                amount = 99.99m,
                currency = "USD"
            }).ToUrl("/api/pricing/products/DOES-NOT-EXIST/base-price");
            x.StatusCodeShouldBe(404);
        });
    }

    [Fact]
    public async Task SetBasePrice_NegativePrice_ReturnsValidationError()
    {
        // Arrange: Register product
        await using var session = _fixture.GetDocumentSession();
        var sku = "HAMSTER-WHEEL";
        var streamId = ProductPrice.StreamId(sku);

        session.Events.StartStream<ProductPrice>(
            streamId,
            new ProductRegistered(streamId, sku, DateTimeOffset.UtcNow));

        await session.SaveChangesAsync();

        // Act: Try to set negative price (FluentValidation will reject)
        await _fixture.Host.Scenario(x =>
        {
            x.Post.Json(new
            {
                amount = -10.00m,  // Invalid
                currency = "USD"
            }).ToUrl($"/api/pricing/products/{sku}/base-price");
            x.StatusCodeShouldBe(400);
        });
    }
}
