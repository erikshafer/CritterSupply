using FluentValidation;

namespace Pricing.Products;

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
