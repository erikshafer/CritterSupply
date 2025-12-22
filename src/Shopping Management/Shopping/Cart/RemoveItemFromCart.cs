using FluentValidation;

namespace Shopping.Cart;

public sealed record RemoveItemFromCart(
    Guid CartId,
    string Sku)
{
    public class RemoveItemFromCartValidator : AbstractValidator<RemoveItemFromCart>
    {
        public RemoveItemFromCartValidator()
        {
            RuleFor(x => x.CartId).NotEmpty();
            RuleFor(x => x.Sku).NotEmpty().MaximumLength(50);
        }
    }
}
