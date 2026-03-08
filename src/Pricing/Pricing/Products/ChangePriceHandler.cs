using Marten;
using Wolverine.Marten;

namespace Pricing.Products;

public static class ChangePriceHandler
{
    public static async Task<(ProductPrice, PriceChanged)> Handle(
        ChangePrice command,
        IDocumentSession session,
        CancellationToken ct)
    {
        var streamId = ProductPrice.StreamId(command.Sku);
        var aggregate = await session.Events.AggregateStreamAsync<ProductPrice>(streamId, token: ct);

        if (aggregate is null)
        {
            throw new InvalidOperationException($"Product with SKU '{command.Sku}' not found.");
        }

        if (aggregate.Status != PriceStatus.Published)
        {
            throw new InvalidOperationException($"Cannot change price for product in {aggregate.Status} status. Product must be in Published status.");
        }

        if (aggregate.BasePrice is null)
        {
            throw new InvalidOperationException($"Product has no base price set. Use SetInitialPrice first.");
        }

        var newPrice = Money.Of(command.NewAmount, command.Currency);

        // Enforce floor/ceiling constraints if set
        if (aggregate.FloorPrice is not null && newPrice < aggregate.FloorPrice)
        {
            throw new InvalidOperationException($"New price {newPrice} is below floor price {aggregate.FloorPrice}.");
        }

        if (aggregate.CeilingPrice is not null && newPrice > aggregate.CeilingPrice)
        {
            throw new InvalidOperationException($"New price {newPrice} exceeds ceiling price {aggregate.CeilingPrice}.");
        }

        var evt = new PriceChanged(
            ProductPriceId: streamId,
            Sku: command.Sku.ToUpperInvariant(),
            OldPrice: aggregate.BasePrice,
            NewPrice: newPrice,
            PreviousPriceSetAt: aggregate.LastChangedAt ?? aggregate.RegisteredAt,
            Reason: command.Reason,
            ChangedBy: command.ChangedBy,
            ChangedAt: command.ChangedAt,
            BulkPricingJobId: command.BulkPricingJobId,
            SourceSuggestionId: command.SourceSuggestionId);

        return (aggregate.Apply(evt), evt);
    }
}
