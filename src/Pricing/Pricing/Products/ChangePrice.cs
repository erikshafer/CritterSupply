using FluentValidation;
using Marten;

namespace Pricing.Products;

/// <summary>
/// Command: Change the base price for a Published product.
/// Requires product to be in Published status (cannot change Unpriced or Discontinued products).
/// Tracks previous price and timestamp for Was/Now display.
/// </summary>
public sealed record ChangePrice(
    string Sku,
    decimal NewAmount,
    string Currency,
    string? Reason,
    Guid ChangedBy,
    DateTimeOffset ChangedAt,
    Guid? BulkPricingJobId = null,
    Guid? SourceSuggestionId = null);

public sealed class ChangePriceValidator : AbstractValidator<ChangePrice>
{
    public ChangePriceValidator()
    {
        RuleFor(x => x.Sku)
            .NotEmpty()
            .WithMessage("SKU is required");

        RuleFor(x => x.NewAmount)
            .GreaterThan(0)
            .WithMessage("New amount must be greater than 0");

        RuleFor(x => x.Currency)
            .NotEmpty()
            .WithMessage("Currency is required")
            .Length(3)
            .WithMessage("Currency must be 3 characters (ISO 4217)");
    }
}

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
