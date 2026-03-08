using Marten;
using Pricing.Products;
using Shouldly;

namespace Pricing.Api.IntegrationTests;

[Collection(IntegrationTestCollection.Name)]
public sealed class GetBulkPricesEndpointTests(TestFixture fixture)
{
    private readonly TestFixture _fixture = fixture;

    [Fact]
    public async Task GetBulkPrices_WithAllExistingSkus_Returns200WithArray()
    {
        // Arrange: Seed 3 priced products
        await using var session = _fixture.GetDocumentSession();
        var skus = new[] { "DOG-FOOD-5LB", "CAT-FOOD-3LB", "BIRD-FOOD-2LB" };

        foreach (var sku in skus)
        {
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
        }

        await session.SaveChangesAsync();

        // Act
        var result = await _fixture.Host.Scenario(x =>
        {
            x.Get.Url("/api/pricing/products?skus=DOG-FOOD-5LB,CAT-FOOD-3LB,BIRD-FOOD-2LB");
            x.StatusCodeShouldBeOk();
        });

        // Assert
        var prices = result.ReadAsJson<List<CurrentPriceView>>();
        prices.ShouldNotBeNull();
        prices.Count.ShouldBe(3);
        prices.Select(p => p.Sku).ShouldBe(skus, ignoreOrder: true);
    }

    [Fact]
    public async Task GetBulkPrices_WithSomeMissingSkus_Returns200WithPartialResults()
    {
        // Arrange: Seed only 2 products
        await using var session = _fixture.GetDocumentSession();
        var existingSkus = new[] { "DOG-FOOD-5LB", "CAT-FOOD-3LB" };

        foreach (var sku in existingSkus)
        {
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
        }

        await session.SaveChangesAsync();

        // Act: Query for 3 SKUs (1 doesn't exist)
        var result = await _fixture.Host.Scenario(x =>
        {
            x.Get.Url("/api/pricing/products?skus=DOG-FOOD-5LB,CAT-FOOD-3LB,NONEXISTENT-SKU");
            x.StatusCodeShouldBeOk(); // Partial results OK, not 404
        });

        // Assert: Only 2 results returned
        var prices = result.ReadAsJson<List<CurrentPriceView>>();
        prices.ShouldNotBeNull();
        prices.Count.ShouldBe(2);
        prices.Select(p => p.Sku).ShouldBe(existingSkus, ignoreOrder: true);
    }

    [Fact]
    public async Task GetBulkPrices_WithEmptySkusParameter_Returns400()
    {
        // Act
        var result = await _fixture.Host.Scenario(x =>
        {
            x.Get.Url("/api/pricing/products?skus=");
            x.StatusCodeShouldBe(400);
        });

        // Assert
        var response = result.ReadAsJson<Dictionary<string, string>>();
        response.ShouldContainKey("message");
        response["message"].ShouldContain("required");
    }

    [Fact]
    public async Task GetBulkPrices_WithMissingSkusParameter_Returns400()
    {
        // Act
        var result = await _fixture.Host.Scenario(x =>
        {
            x.Get.Url("/api/pricing/products");
            x.StatusCodeShouldBe(400);
        });

        // Assert
        var response = result.ReadAsJson<Dictionary<string, string>>();
        response.ShouldContainKey("message");
        response["message"].ShouldContain("required");
    }

    [Fact]
    public async Task GetBulkPrices_WithMoreThan50Skus_Returns400()
    {
        // Arrange: Generate 51 SKUs
        var skus = Enumerable.Range(1, 51).Select(i => $"SKU-{i:D3}").ToList();
        var skusQuery = string.Join(",", skus);

        // Act
        var result = await _fixture.Host.Scenario(x =>
        {
            x.Get.Url($"/api/pricing/products?skus={skusQuery}");
            x.StatusCodeShouldBe(400);
        });

        // Assert
        var response = result.ReadAsJson<Dictionary<string, string>>();
        response.ShouldContainKey("message");
        response["message"].ShouldContain("Maximum 50 SKUs");
    }

    [Fact]
    public async Task GetBulkPrices_WithDuplicateSkus_DeduplicatesAndReturnsUnique()
    {
        // Arrange: Seed 1 product
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

        // Act: Query with duplicates
        var result = await _fixture.Host.Scenario(x =>
        {
            x.Get.Url("/api/pricing/products?skus=DOG-FOOD-5LB,DOG-FOOD-5LB,DOG-FOOD-5LB");
            x.StatusCodeShouldBeOk();
        });

        // Assert: Only 1 result
        var prices = result.ReadAsJson<List<CurrentPriceView>>();
        prices.ShouldNotBeNull();
        prices.Count.ShouldBe(1);
    }

    [Fact]
    public async Task GetBulkPrices_With50Skus_CompletesUnder100ms()
    {
        // Arrange: Seed 50 priced products
        await using var session = _fixture.GetDocumentSession();
        var skus = Enumerable.Range(1, 50).Select(i => $"SKU-{i:D3}").ToList();

        foreach (var sku in skus)
        {
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
        }

        await session.SaveChangesAsync();

        // Act
        var skusQuery = string.Join(",", skus);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var result = await _fixture.Host.Scenario(x =>
        {
            x.Get.Url($"/api/pricing/products?skus={skusQuery}");
            x.StatusCodeShouldBeOk();
        });

        stopwatch.Stop();

        // Assert: Performance SLA
        var prices = result.ReadAsJson<List<CurrentPriceView>>();
        prices.Count.ShouldBe(50);
        stopwatch.ElapsedMilliseconds.ShouldBeLessThan(100);
    }
}
