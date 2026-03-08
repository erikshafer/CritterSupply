using Marten;
using Wolverine.Marten;

namespace Pricing.Products;

public static class SetInitialPriceHandler
{
    public static async Task<(ProductPrice, InitialPriceSet)> Handle(
        SetInitialPrice command,
        IDocumentSession session,
        CancellationToken ct)
    {
        var streamId = ProductPrice.StreamId(command.Sku);
        var aggregate = await session.Events.AggregateStreamAsync<ProductPrice>(streamId, token: ct);

        if (aggregate is null)
        {
            throw new InvalidOperationException($"Product with SKU '{command.Sku}' not found. Must register product via ProductAdded integration event first.");
        }

        if (aggregate.Status != PriceStatus.Unpriced)
        {
            throw new InvalidOperationException($"Cannot set initial price for product in {aggregate.Status} status. Product must be in Unpriced status.");
        }

        var basePrice = Money.Of(command.Amount, command.Currency);
        var floorPrice = command.FloorAmount.HasValue ? Money.Of(command.FloorAmount.Value, command.Currency) : null;
        var ceilingPrice = command.CeilingAmount.HasValue ? Money.Of(command.CeilingAmount.Value, command.Currency) : null;

        var evt = new InitialPriceSet(
            ProductPriceId: streamId,
            Sku: command.Sku.ToUpperInvariant(),
            Price: basePrice,
            FloorPrice: floorPrice,
            CeilingPrice: ceilingPrice,
            SetBy: command.SetBy,
            PricedAt: command.PricedAt);

        return (aggregate.Apply(evt), evt);
    }
}
