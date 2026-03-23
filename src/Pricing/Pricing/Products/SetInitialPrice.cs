using FluentValidation;
using Marten;
using Wolverine.Marten;

namespace Pricing.Products;

/// <summary>
/// Command: Set initial base price for an Unpriced product.
/// Transitions product from Unpriced → Published status.
/// Can optionally set floor/ceiling constraints.
/// </summary>
public sealed record SetInitialPrice(
    string Sku,
    decimal Amount,
    string Currency,
    decimal? FloorAmount,
    decimal? CeilingAmount,
    Guid SetBy,
    DateTimeOffset PricedAt);

public sealed class SetInitialPriceValidator : AbstractValidator<SetInitialPrice>
{
    public SetInitialPriceValidator()
    {
        RuleFor(x => x.Sku)
            .NotEmpty()
            .WithMessage("SKU is required");

        RuleFor(x => x.Amount)
            .GreaterThan(0)
            .WithMessage("Amount must be greater than 0");

        RuleFor(x => x.Currency)
            .NotEmpty()
            .WithMessage("Currency is required")
            .Length(3)
            .WithMessage("Currency must be 3 characters (ISO 4217)");

        RuleFor(x => x.FloorAmount)
            .GreaterThan(0)
            .When(x => x.FloorAmount.HasValue)
            .WithMessage("Floor amount must be greater than 0");

        RuleFor(x => x.CeilingAmount)
            .GreaterThan(0)
            .When(x => x.CeilingAmount.HasValue)
            .WithMessage("Ceiling amount must be greater than 0");

        RuleFor(x => x)
            .Must(x => !x.FloorAmount.HasValue || x.Amount >= x.FloorAmount.Value)
            .WithMessage("Base price must be >= floor price")
            .When(x => x.FloorAmount.HasValue);

        RuleFor(x => x)
            .Must(x => !x.CeilingAmount.HasValue || x.Amount <= x.CeilingAmount.Value)
            .WithMessage("Base price must be <= ceiling price")
            .When(x => x.CeilingAmount.HasValue);

        RuleFor(x => x)
            .Must(x => !x.FloorAmount.HasValue || !x.CeilingAmount.HasValue || x.FloorAmount.Value <= x.CeilingAmount.Value)
            .WithMessage("Floor price must be <= ceiling price")
            .When(x => x.FloorAmount.HasValue && x.CeilingAmount.HasValue);
    }
}

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
