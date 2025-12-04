using FluentValidation;

namespace Payments.Processing;

/// <summary>
/// FluentValidation validator for RefundRequested commands.
/// Validates required fields before refund processing.
/// </summary>
public sealed class RefundRequestedValidator : AbstractValidator<RefundRequested>
{
    public RefundRequestedValidator()
    {
        // Requirement 5.1: PaymentId must not be empty
        RuleFor(x => x.PaymentId)
            .NotEmpty()
            .WithMessage("Payment identifier is required");

        // Requirement 5.3: Amount must be greater than zero
        RuleFor(x => x.Amount)
            .GreaterThan(0)
            .WithMessage("Refund amount must be greater than zero");
    }
}
