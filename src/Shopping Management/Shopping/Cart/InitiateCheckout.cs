using FluentValidation;

namespace Shopping.Cart;

public sealed record InitiateCheckout(
    Guid CartId)
{
    public class InitiateCheckoutValidator : AbstractValidator<InitiateCheckout>
    {
        public InitiateCheckoutValidator()
        {
            RuleFor(x => x.CartId).NotEmpty();
        }
    }
}
