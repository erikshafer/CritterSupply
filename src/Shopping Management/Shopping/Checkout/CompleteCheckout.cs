using FluentValidation;

namespace Shopping.Checkout;

public sealed record CompleteCheckout(
    Guid CheckoutId)
{
    public class CompleteCheckoutValidator : AbstractValidator<CompleteCheckout>
    {
        public CompleteCheckoutValidator()
        {
            RuleFor(x => x.CheckoutId).NotEmpty();
        }
    }
}
