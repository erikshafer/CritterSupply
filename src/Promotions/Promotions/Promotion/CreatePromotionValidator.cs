using FluentValidation;

namespace Promotions.Promotion;

public sealed class CreatePromotionValidator : AbstractValidator<CreatePromotion>
{
    public CreatePromotionValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(200)
            .WithMessage("Promotion name is required and must be 200 characters or less");

        RuleFor(x => x.Description)
            .NotEmpty()
            .MaximumLength(1000)
            .WithMessage("Promotion description is required and must be 1000 characters or less");

        RuleFor(x => x.DiscountType)
            .IsInEnum()
            .WithMessage("Invalid discount type");

        RuleFor(x => x.DiscountValue)
            .GreaterThan(0)
            .WithMessage("Discount value must be greater than 0");

        When(x => x.DiscountType == DiscountType.PercentageOff, () =>
        {
            RuleFor(x => x.DiscountValue)
                .LessThanOrEqualTo(100)
                .WithMessage("Percentage discount must be between 0 and 100");
        });

        RuleFor(x => x.EndDate)
            .GreaterThan(x => x.StartDate)
            .WithMessage("End date must be after start date");

        When(x => x.UsageLimit.HasValue, () =>
        {
            RuleFor(x => x.UsageLimit!.Value)
                .GreaterThan(0)
                .WithMessage("Usage limit must be greater than 0");
        });
    }
}
