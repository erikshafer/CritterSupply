using FluentValidation;

namespace Promotions.Coupon;

public sealed class IssueCouponValidator : AbstractValidator<IssueCoupon>
{
    public IssueCouponValidator()
    {
        RuleFor(x => x.CouponCode)
            .NotEmpty()
            .MaximumLength(50)
            .Matches("^[A-Z0-9-]+$")
            .WithMessage("Coupon code is required, must be 50 characters or less, and contain only uppercase letters, numbers, and hyphens");

        RuleFor(x => x.PromotionId)
            .NotEmpty()
            .WithMessage("PromotionId is required");
    }
}
