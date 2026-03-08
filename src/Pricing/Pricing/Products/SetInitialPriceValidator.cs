using FluentValidation;

namespace Pricing.Products;

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
