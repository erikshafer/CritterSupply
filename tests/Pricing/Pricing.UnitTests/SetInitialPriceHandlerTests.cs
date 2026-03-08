using Marten;
using NSubstitute;
using Pricing.Products;
using Shouldly;
using Xunit;

namespace Pricing.UnitTests;

public sealed class SetInitialPriceHandlerTests
{
    private readonly DateTimeOffset _testTime = new(2026, 3, 8, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Handle_WithUnpricedProduct_ReturnsInitialPriceSet()
    {
        var command = new SetInitialPrice(
            Sku: "DOG-FOOD-5LB",
            Amount: 29.99m,
            Currency: "USD",
            FloorAmount: null,
            CeilingAmount: null,
            SetBy: Guid.NewGuid(),
            PricedAt: _testTime);

        var session = Substitute.For<IDocumentSession>();
        var streamId = ProductPrice.StreamId(command.Sku);
        var aggregate = ProductPrice.Create("DOG-FOOD-5LB", _testTime.AddDays(-1));

        session.Events.AggregateStreamAsync<ProductPrice>(streamId, token: Arg.Any<CancellationToken>())
            .Returns(aggregate);

        var (updatedAggregate, evt) = await SetInitialPriceHandler.Handle(command, session, CancellationToken.None);

        updatedAggregate.Status.ShouldBe(PriceStatus.Published);
        updatedAggregate.BasePrice.ShouldBe(Money.Of(29.99m, "USD"));
        evt.Sku.ShouldBe("DOG-FOOD-5LB");
        evt.Price.ShouldBe(Money.Of(29.99m, "USD"));
    }

    [Fact]
    public async Task Handle_WithFloorAndCeiling_SetsConstraints()
    {
        var command = new SetInitialPrice(
            Sku: "DOG-FOOD-5LB",
            Amount: 29.99m,
            Currency: "USD",
            FloorAmount: 19.99m,
            CeilingAmount: 39.99m,
            SetBy: Guid.NewGuid(),
            PricedAt: _testTime);

        var session = Substitute.For<IDocumentSession>();
        var streamId = ProductPrice.StreamId(command.Sku);
        var aggregate = ProductPrice.Create("DOG-FOOD-5LB", _testTime.AddDays(-1));

        session.Events.AggregateStreamAsync<ProductPrice>(streamId, token: Arg.Any<CancellationToken>())
            .Returns(aggregate);

        var (updatedAggregate, evt) = await SetInitialPriceHandler.Handle(command, session, CancellationToken.None);

        updatedAggregate.FloorPrice.ShouldBe(Money.Of(19.99m, "USD"));
        updatedAggregate.CeilingPrice.ShouldBe(Money.Of(39.99m, "USD"));
        evt.FloorPrice.ShouldBe(Money.Of(19.99m, "USD"));
        evt.CeilingPrice.ShouldBe(Money.Of(39.99m, "USD"));
    }

    [Fact]
    public async Task Handle_WithNonExistentProduct_ThrowsInvalidOperationException()
    {
        var command = new SetInitialPrice(
            Sku: "NONEXISTENT-SKU",
            Amount: 29.99m,
            Currency: "USD",
            FloorAmount: null,
            CeilingAmount: null,
            SetBy: Guid.NewGuid(),
            PricedAt: _testTime);

        var session = Substitute.For<IDocumentSession>();
        var streamId = ProductPrice.StreamId(command.Sku);

        session.Events.AggregateStreamAsync<ProductPrice>(streamId, token: Arg.Any<CancellationToken>())
            .Returns((ProductPrice?)null);

        var exception = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await SetInitialPriceHandler.Handle(command, session, CancellationToken.None));

        exception.Message.ShouldContain("not found");
    }

    [Fact]
    public async Task Handle_WithPublishedProduct_ThrowsInvalidOperationException()
    {
        var command = new SetInitialPrice(
            Sku: "DOG-FOOD-5LB",
            Amount: 29.99m,
            Currency: "USD",
            FloorAmount: null,
            CeilingAmount: null,
            SetBy: Guid.NewGuid(),
            PricedAt: _testTime);

        var session = Substitute.For<IDocumentSession>();
        var streamId = ProductPrice.StreamId(command.Sku);
        var aggregate = ProductPrice.Create("DOG-FOOD-5LB", _testTime.AddDays(-1))
            .Apply(new InitialPriceSet(
                ProductPriceId: streamId,
                Sku: "DOG-FOOD-5LB",
                Price: Money.Of(19.99m, "USD"),
                FloorPrice: null,
                CeilingPrice: null,
                SetBy: Guid.NewGuid(),
                PricedAt: _testTime.AddDays(-1)));

        session.Events.AggregateStreamAsync<ProductPrice>(streamId, token: Arg.Any<CancellationToken>())
            .Returns(aggregate);

        var exception = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await SetInitialPriceHandler.Handle(command, session, CancellationToken.None));

        exception.Message.ShouldContain("Published status");
    }
}
