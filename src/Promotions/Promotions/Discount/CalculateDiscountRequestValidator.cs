using FluentValidation;

namespace Promotions.Discount;

public sealed class CalculateDiscountRequestValidator : AbstractValidator<CalculateDiscountRequest>
{
    public CalculateDiscountRequestValidator()
    {
        RuleFor(x => x.CartItems)
            .NotEmpty()
            .WithMessage("Cart must contain at least one item");

        RuleForEach(x => x.CartItems)
            .ChildRules(item =>
            {
                item.RuleFor(x => x.Sku)
                    .NotEmpty()
                    .WithMessage("SKU is required");

                item.RuleFor(x => x.Quantity)
                    .GreaterThan(0)
                    .WithMessage("Quantity must be greater than 0");

                item.RuleFor(x => x.UnitPrice)
                    .GreaterThanOrEqualTo(0)
                    .WithMessage("Unit price cannot be negative");
            });

        RuleFor(x => x.CouponCodes)
            .NotNull()
            .WithMessage("CouponCodes cannot be null (use empty list if no coupons)");

        RuleFor(x => x.CouponCodes)
            .Must(codes => codes.Count <= 1)
            .WithMessage("Phase 1 only supports a single coupon per cart")
            .When(x => x.CouponCodes.Count > 0);
    }
}
