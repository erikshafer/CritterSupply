using FluentValidation;

namespace Promotions.Coupon;

public sealed class RevokeCouponValidator : AbstractValidator<RevokeCoupon>
{
    public RevokeCouponValidator()
    {
        RuleFor(x => x.CouponCode)
            .NotEmpty()
            .WithMessage("Coupon code is required");

        RuleFor(x => x.Reason)
            .NotEmpty()
            .WithMessage("Reason for revocation is required");
    }
}
