using FluentValidation;

namespace Promotions.Promotion;

public sealed class RecordPromotionRedemptionValidator : AbstractValidator<RecordPromotionRedemption>
{
    public RecordPromotionRedemptionValidator()
    {
        RuleFor(x => x.PromotionId)
            .NotEmpty()
            .WithMessage("Promotion ID is required");

        RuleFor(x => x.OrderId)
            .NotEmpty()
            .WithMessage("Order ID is required");

        RuleFor(x => x.CustomerId)
            .NotEmpty()
            .WithMessage("Customer ID is required");

        RuleFor(x => x.CouponCode)
            .NotEmpty()
            .WithMessage("Coupon code is required");

        RuleFor(x => x.RedeemedAt)
            .NotEmpty()
            .WithMessage("Redemption timestamp is required");
    }
}
