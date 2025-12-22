using FluentValidation;

namespace Shopping.Cart;

public sealed record ChangeItemQuantity(
    Guid CartId,
    string Sku,
    int NewQuantity)
{
    public class ChangeItemQuantityValidator : AbstractValidator<ChangeItemQuantity>
    {
        public ChangeItemQuantityValidator()
        {
            RuleFor(x => x.CartId).NotEmpty();
            RuleFor(x => x.Sku).NotEmpty().MaximumLength(50);
            RuleFor(x => x.NewQuantity).GreaterThan(0);
        }
    }
}
