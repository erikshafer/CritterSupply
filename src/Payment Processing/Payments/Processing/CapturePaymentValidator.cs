using FluentValidation;

namespace Payments.Processing;

/// <summary>
/// Validator for CapturePayment commands.
/// </summary>
public sealed class CapturePaymentValidator : AbstractValidator<CapturePayment>
{
    public CapturePaymentValidator()
    {
        RuleFor(x => x.PaymentId).NotEmpty();
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.AmountToCapture)
            .GreaterThan(0)
            .When(x => x.AmountToCapture.HasValue);
    }
}
