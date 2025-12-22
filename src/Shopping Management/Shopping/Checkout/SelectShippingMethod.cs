using FluentValidation;

namespace Shopping.Checkout;

public sealed record SelectShippingMethod(
    Guid CheckoutId,
    string ShippingMethod,
    decimal ShippingCost)
{
    public class SelectShippingMethodValidator : AbstractValidator<SelectShippingMethod>
    {
        public SelectShippingMethodValidator()
        {
            RuleFor(x => x.CheckoutId).NotEmpty();
            RuleFor(x => x.ShippingMethod).NotEmpty().MaximumLength(100);
            RuleFor(x => x.ShippingCost).GreaterThanOrEqualTo(0);
        }
    }
}
