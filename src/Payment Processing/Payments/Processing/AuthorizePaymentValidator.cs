using FluentValidation;

namespace Payments.Processing;

/// <summary>
/// Validator for AuthorizePayment commands.
/// </summary>
public sealed class AuthorizePaymentValidator : AbstractValidator<AuthorizePayment>
{
    public AuthorizePaymentValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.Currency).NotEmpty().MaximumLength(3);
        RuleFor(x => x.PaymentMethodToken).NotEmpty().MaximumLength(256);
    }
}
