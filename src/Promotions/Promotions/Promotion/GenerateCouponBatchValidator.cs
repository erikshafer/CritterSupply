using FluentValidation;

namespace Promotions.Promotion;

public sealed class GenerateCouponBatchValidator : AbstractValidator<GenerateCouponBatch>
{
    public GenerateCouponBatchValidator()
    {
        RuleFor(x => x.PromotionId)
            .NotEmpty()
            .WithMessage("Promotion ID is required");

        RuleFor(x => x.Prefix)
            .NotEmpty()
            .WithMessage("Coupon prefix is required")
            .MaximumLength(20)
            .WithMessage("Coupon prefix must be 20 characters or less");

        RuleFor(x => x.Count)
            .GreaterThan(0)
            .WithMessage("Count must be greater than 0")
            .LessThanOrEqualTo(10000)
            .WithMessage("Cannot generate more than 10,000 coupons in a single batch");
    }
}
