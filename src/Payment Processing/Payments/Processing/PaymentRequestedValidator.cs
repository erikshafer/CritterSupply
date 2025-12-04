using FluentValidation;

namespace Payments.Processing;

/// <summary>
/// FluentValidation validator for PaymentRequested commands.
/// Validates all required fields before payment processing.
/// </summary>
public sealed class PaymentRequestedValidator : AbstractValidator<PaymentRequested>
{
    public PaymentRequestedValidator()
    {
        // Requirement 4.1: Zero or negative amount
        RuleFor(x => x.Amount)
            .GreaterThan(0)
            .WithMessage("Payment amount must be greater than zero");

        // Requirement 4.2: Missing order identifier
        RuleFor(x => x.OrderId)
            .NotEmpty()
            .WithMessage("Order identifier is required");

        // Missing customer identifier
        RuleFor(x => x.CustomerId)
            .NotEmpty()
            .WithMessage("Customer identifier is required");

        // Requirement 4.3: Missing payment method token
        RuleFor(x => x.PaymentMethodToken)
            .NotEmpty()
            .WithMessage("Payment method token is required");

        // Requirement 4.4: Missing currency
        RuleFor(x => x.Currency)
            .NotEmpty()
            .WithMessage("Currency is required");
    }
}
