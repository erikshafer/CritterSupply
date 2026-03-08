using Marten;
using Pricing.Products;
using Shouldly;

namespace Pricing.Api.IntegrationTests;

[Collection(IntegrationTestCollection.Name)]
public sealed class GetPriceEndpointTests(TestFixture fixture) : IAsyncLifetime
{
    private readonly TestFixture _fixture = fixture;

    public Task InitializeAsync() => _fixture.CleanAllDataAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetPrice_WithExistingSku_Returns200WithPriceView()
    {
        // Arrange: Seed a priced product
        await using var session = _fixture.GetDocumentSession();
        var sku = "DOG-FOOD-5LB";
        var streamId = ProductPrice.StreamId(sku);

        session.Events.StartStream<ProductPrice>(
            streamId,
            new ProductRegistered(streamId, sku, DateTimeOffset.UtcNow.AddDays(-1)),
            new InitialPriceSet(
                ProductPriceId: streamId,
                Sku: sku,
                Price: Money.Of(29.99m, "USD"),
                FloorPrice: null,
                CeilingPrice: null,
                SetBy: Guid.NewGuid(),
                PricedAt: DateTimeOffset.UtcNow));

        await session.SaveChangesAsync();

        // Act
        var result = await _fixture.Host.Scenario(x =>
        {
            x.Get.Url($"/api/pricing/products/{sku}");
            x.StatusCodeShouldBeOk();
        });

        // Assert
        var priceView = result.ReadAsJson<CurrentPriceView>();
        priceView.ShouldNotBeNull();
        priceView.Sku.ShouldBe(sku);
        priceView.BasePrice.ShouldBe(29.99m);
        priceView.Currency.ShouldBe("USD");
        priceView.Status.ShouldBe(PriceStatus.Published);
    }

    [Fact]
    public async Task GetPrice_WithNonExistentSku_Returns404()
    {
        // Act
        var result = await _fixture.Host.Scenario(x =>
        {
            x.Get.Url("/api/pricing/products/NONEXISTENT-SKU");
            x.StatusCodeShouldBe(404);
        });

        // Assert
        var response = result.ReadAsJson<Dictionary<string, string>>();
        response.ShouldContainKey("message");
        response["message"].ShouldContain("has not been registered");
    }

    [Fact]
    public async Task GetPrice_WithLowercaseSku_NormalizesToUppercase()
    {
        // Arrange: Seed with uppercase SKU
        await using var session = _fixture.GetDocumentSession();
        var sku = "CAT-FOOD-3LB";
        var streamId = ProductPrice.StreamId(sku);

        session.Events.StartStream<ProductPrice>(
            streamId,
            new ProductRegistered(streamId, sku, DateTimeOffset.UtcNow.AddDays(-1)),
            new InitialPriceSet(
                ProductPriceId: streamId,
                Sku: sku,
                Price: Money.Of(19.99m, "USD"),
                FloorPrice: null,
                CeilingPrice: null,
                SetBy: Guid.NewGuid(),
                PricedAt: DateTimeOffset.UtcNow));

        await session.SaveChangesAsync();

        // Act: Query with lowercase
        var result = await _fixture.Host.Scenario(x =>
        {
            x.Get.Url("/api/pricing/products/cat-food-3lb");
            x.StatusCodeShouldBeOk();
        });

        // Assert
        var priceView = result.ReadAsJson<CurrentPriceView>();
        priceView.ShouldNotBeNull();
        priceView.Sku.ShouldBe("CAT-FOOD-3LB");
    }

    [Fact]
    public async Task GetPrice_WithUnpricedProduct_Returns404()
    {
        // Arrange: Seed with Unpriced status (ProductRegistered only, no InitialPriceSet)
        await using var session = _fixture.GetDocumentSession();
        var sku = "BIRD-FOOD-2LB";
        var streamId = ProductPrice.StreamId(sku);

        session.Events.StartStream<ProductPrice>(
            streamId,
            new ProductRegistered(streamId, sku, DateTimeOffset.UtcNow));

        await session.SaveChangesAsync();

        // Act: CurrentPriceView projection requires InitialPriceSet to exist
        var result = await _fixture.Host.Scenario(x =>
        {
            x.Get.Url($"/api/pricing/products/{sku}");
            x.StatusCodeShouldBe(404);
        });
    }
}
