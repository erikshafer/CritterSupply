using FluentValidation;

namespace Orders.Placement;

/// <summary>
/// Command to cancel an existing order.
/// Triggers compensation: releases inventory reservations and refunds captured payment.
/// Guard: An order cannot be cancelled after it has been shipped.
/// </summary>
public sealed record CancelOrder(
    Guid OrderId,
    string Reason)
{
    public class CancelOrderValidator : AbstractValidator<CancelOrder>
    {
        public CancelOrderValidator()
        {
            RuleFor(x => x.OrderId)
                .NotEmpty()
                .WithMessage("Order identifier is required");

            RuleFor(x => x.Reason)
                .NotEmpty()
                .WithMessage("Cancellation reason is required");
        }
    }
}
