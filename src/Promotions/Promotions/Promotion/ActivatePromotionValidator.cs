using FluentValidation;

namespace Promotions.Promotion;

public sealed class ActivatePromotionValidator : AbstractValidator<ActivatePromotion>
{
    public ActivatePromotionValidator()
    {
        RuleFor(x => x.PromotionId)
            .NotEmpty()
            .WithMessage("PromotionId is required");
    }
}
