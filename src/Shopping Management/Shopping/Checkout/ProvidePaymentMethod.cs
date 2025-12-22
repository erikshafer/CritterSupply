using FluentValidation;

namespace Shopping.Checkout;

public sealed record ProvidePaymentMethod(
    Guid CheckoutId,
    string PaymentMethodToken)
{
    public class ProvidePaymentMethodValidator : AbstractValidator<ProvidePaymentMethod>
    {
        public ProvidePaymentMethodValidator()
        {
            RuleFor(x => x.CheckoutId).NotEmpty();
            RuleFor(x => x.PaymentMethodToken).NotEmpty().MaximumLength(500);
        }
    }
}
