using FluentValidation;

namespace Orders.Placement;

/// <summary>
/// FluentValidation validator for CheckoutCompleted integration events.
/// Validates all required fields before saga creation.
/// </summary>
public sealed class CheckoutCompletedValidator : AbstractValidator<CheckoutCompleted>
{
    public CheckoutCompletedValidator()
    {
        // Requirement: Order ID must be provided
        RuleFor(x => x.OrderId)
            .NotEmpty()
            .WithMessage("Order identifier is required");

        // Requirement: Checkout ID must be provided
        RuleFor(x => x.CheckoutId)
            .NotEmpty()
            .WithMessage("Checkout identifier is required");

        // Requirement 3.4: Missing customer identifier
        RuleFor(x => x.CustomerId)
            .NotNull()
            .NotEmpty()
            .WithMessage("Customer identifier is required");

        // Requirement 3.1: Zero line items
        RuleFor(x => x.LineItems)
            .NotEmpty()
            .WithMessage("Order must contain at least one line item");

        // Requirements 3.2, 3.3: Invalid line item quantity or price
        RuleForEach(x => x.LineItems)
            .ChildRules(item =>
            {
                item.RuleFor(x => x.Quantity)
                    .GreaterThan(0)
                    .WithMessage("Quantity must be positive");

                item.RuleFor(x => x.PriceAtPurchase)
                    .GreaterThan(0)
                    .WithMessage("Price must be positive");
            });

        // Requirement 3.5: Missing shipping address
        RuleFor(x => x.ShippingAddress)
            .NotNull()
            .WithMessage("Shipping address is required");

        // Requirement: Shipping method must be provided
        RuleFor(x => x.ShippingMethod)
            .NotEmpty()
            .WithMessage("Shipping method is required");

        // Requirement: Shipping cost must be non-negative
        RuleFor(x => x.ShippingCost)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Shipping cost must be non-negative");

        // Requirement 3.6: Missing payment method token
        RuleFor(x => x.PaymentMethodToken)
            .NotEmpty()
            .WithMessage("Payment method token is required");
    }
}
