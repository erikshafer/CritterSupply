using Marten;
using NSubstitute;
using Pricing.Products;
using Shouldly;
using Xunit;

namespace Pricing.UnitTests;

public sealed class ChangePriceHandlerTests
{
    private readonly DateTimeOffset _testTime = new(2026, 3, 8, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Handle_WithPublishedProduct_ReturnsPriceChanged()
    {
        var command = new ChangePrice(
            Sku: "DOG-FOOD-5LB",
            NewAmount: 24.99m,
            Currency: "USD",
            Reason: null,
            ChangedBy: Guid.NewGuid(),
            ChangedAt: _testTime);

        var session = Substitute.For<IDocumentSession>();
        var streamId = ProductPrice.StreamId(command.Sku);
        var aggregate = ProductPrice.Create("DOG-FOOD-5LB", _testTime.AddDays(-2))
            .Apply(new InitialPriceSet(
                ProductPriceId: streamId,
                Sku: "DOG-FOOD-5LB",
                Price: Money.Of(29.99m, "USD"),
                FloorPrice: null,
                CeilingPrice: null,
                SetBy: Guid.NewGuid(),
                PricedAt: _testTime.AddDays(-1)));

        session.Events.AggregateStreamAsync<ProductPrice>(streamId, token: Arg.Any<CancellationToken>())
            .Returns(aggregate);

        var (updatedAggregate, evt) = await ChangePriceHandler.Handle(command, session, CancellationToken.None);

        updatedAggregate.BasePrice.ShouldBe(Money.Of(24.99m, "USD"));
        updatedAggregate.PreviousBasePrice.ShouldBe(Money.Of(29.99m, "USD"));
        evt.NewPrice.ShouldBe(Money.Of(24.99m, "USD"));
        evt.OldPrice.ShouldBe(Money.Of(29.99m, "USD"));
    }

    [Fact]
    public async Task Handle_WithPriceBelowFloor_ThrowsInvalidOperationException()
    {
        var command = new ChangePrice(
            Sku: "DOG-FOOD-5LB",
            NewAmount: 15.99m,
            Currency: "USD",
            Reason: null,
            ChangedBy: Guid.NewGuid(),
            ChangedAt: _testTime);

        var session = Substitute.For<IDocumentSession>();
        var streamId = ProductPrice.StreamId(command.Sku);
        var aggregate = ProductPrice.Create("DOG-FOOD-5LB", _testTime.AddDays(-2))
            .Apply(new InitialPriceSet(
                ProductPriceId: streamId,
                Sku: "DOG-FOOD-5LB",
                Price: Money.Of(29.99m, "USD"),
                FloorPrice: Money.Of(19.99m, "USD"),
                CeilingPrice: null,
                SetBy: Guid.NewGuid(),
                PricedAt: _testTime.AddDays(-1)));

        session.Events.AggregateStreamAsync<ProductPrice>(streamId, token: Arg.Any<CancellationToken>())
            .Returns(aggregate);

        var exception = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await ChangePriceHandler.Handle(command, session, CancellationToken.None));

        exception.Message.ShouldContain("below floor price");
    }

    [Fact]
    public async Task Handle_WithPriceAboveCeiling_ThrowsInvalidOperationException()
    {
        var command = new ChangePrice(
            Sku: "DOG-FOOD-5LB",
            NewAmount: 44.99m,
            Currency: "USD",
            Reason: null,
            ChangedBy: Guid.NewGuid(),
            ChangedAt: _testTime);

        var session = Substitute.For<IDocumentSession>();
        var streamId = ProductPrice.StreamId(command.Sku);
        var aggregate = ProductPrice.Create("DOG-FOOD-5LB", _testTime.AddDays(-2))
            .Apply(new InitialPriceSet(
                ProductPriceId: streamId,
                Sku: "DOG-FOOD-5LB",
                Price: Money.Of(29.99m, "USD"),
                FloorPrice: null,
                CeilingPrice: Money.Of(39.99m, "USD"),
                SetBy: Guid.NewGuid(),
                PricedAt: _testTime.AddDays(-1)));

        session.Events.AggregateStreamAsync<ProductPrice>(streamId, token: Arg.Any<CancellationToken>())
            .Returns(aggregate);

        var exception = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await ChangePriceHandler.Handle(command, session, CancellationToken.None));

        exception.Message.ShouldContain("exceeds ceiling price");
    }

    [Fact]
    public async Task Handle_WithNonExistentProduct_ThrowsInvalidOperationException()
    {
        var command = new ChangePrice(
            Sku: "NONEXISTENT-SKU",
            NewAmount: 29.99m,
            Currency: "USD",
            Reason: null,
            ChangedBy: Guid.NewGuid(),
            ChangedAt: _testTime);

        var session = Substitute.For<IDocumentSession>();
        var streamId = ProductPrice.StreamId(command.Sku);

        session.Events.AggregateStreamAsync<ProductPrice>(streamId, token: Arg.Any<CancellationToken>())
            .Returns((ProductPrice?)null);

        var exception = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await ChangePriceHandler.Handle(command, session, CancellationToken.None));

        exception.Message.ShouldContain("not found");
    }

    [Fact]
    public async Task Handle_WithUnpricedProduct_ThrowsInvalidOperationException()
    {
        var command = new ChangePrice(
            Sku: "DOG-FOOD-5LB",
            NewAmount: 29.99m,
            Currency: "USD",
            Reason: null,
            ChangedBy: Guid.NewGuid(),
            ChangedAt: _testTime);

        var session = Substitute.For<IDocumentSession>();
        var streamId = ProductPrice.StreamId(command.Sku);
        var aggregate = ProductPrice.Create("DOG-FOOD-5LB", _testTime.AddDays(-1));

        session.Events.AggregateStreamAsync<ProductPrice>(streamId, token: Arg.Any<CancellationToken>())
            .Returns(aggregate);

        var exception = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await ChangePriceHandler.Handle(command, session, CancellationToken.None));

        exception.Message.ShouldContain("Published status");
    }

    [Fact]
    public async Task Handle_WithDiscontinuedProduct_ThrowsInvalidOperationException()
    {
        var command = new ChangePrice(
            Sku: "DOG-FOOD-5LB",
            NewAmount: 29.99m,
            Currency: "USD",
            Reason: null,
            ChangedBy: Guid.NewGuid(),
            ChangedAt: _testTime);

        var session = Substitute.For<IDocumentSession>();
        var streamId = ProductPrice.StreamId(command.Sku);
        var aggregate = ProductPrice.Create("DOG-FOOD-5LB", _testTime.AddDays(-3))
            .Apply(new InitialPriceSet(
                ProductPriceId: streamId,
                Sku: "DOG-FOOD-5LB",
                Price: Money.Of(29.99m, "USD"),
                FloorPrice: null,
                CeilingPrice: null,
                SetBy: Guid.NewGuid(),
                PricedAt: _testTime.AddDays(-2)))
            .Apply(new PriceDiscontinued(
                ProductPriceId: streamId,
                Sku: "DOG-FOOD-5LB",
                
                DiscontinuedAt: _testTime.AddDays(-1)));

        session.Events.AggregateStreamAsync<ProductPrice>(streamId, token: Arg.Any<CancellationToken>())
            .Returns(aggregate);

        var exception = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await ChangePriceHandler.Handle(command, session, CancellationToken.None));

        exception.Message.ShouldContain("Discontinued status");
    }
}
