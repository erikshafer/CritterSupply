using FluentValidation;

namespace Orders.Placement;

/// <summary>
/// Command to change the shipping address on an existing order before fulfillment hand-off.
///
/// Eligibility window (M45.1 / S4): allowed in <c>Placed</c>, <c>PendingPayment</c>,
/// <c>PaymentConfirmed</c>, <c>InventoryReserved</c>, <c>OnHold</c>. Disallowed once the order
/// reaches <c>InventoryCommitted</c> (Fulfillment has been instructed to pick) or any later
/// status — a recall / re-pick coordination is required and is deferred to the Orders remaster
/// per <c>docs/planning/milestones/m45-1-order-post-placement-modifications-gap-memo.md</c>.
///
/// HTTP guard: <see cref="ChangeShippingAddressEndpoint"/> validates eligibility and returns a
/// 409 with a clear message before publishing this command.
/// Saga guard: <see cref="Order.Handle(ChangeShippingAddress)"/> re-validates and silently
/// ignores ineligible commands (idempotent under at-least-once delivery).
/// </summary>
public sealed record ChangeShippingAddress(
    Guid OrderId,
    ShippingAddress NewShippingAddress,
    string Reason)
{
    public class ChangeShippingAddressValidator : AbstractValidator<ChangeShippingAddress>
    {
        public ChangeShippingAddressValidator()
        {
            RuleFor(x => x.OrderId)
                .NotEmpty()
                .WithMessage("Order identifier is required");

            RuleFor(x => x.Reason)
                .NotEmpty()
                .WithMessage("Reason for the address change is required");

            RuleFor(x => x.NewShippingAddress)
                .NotNull()
                .WithMessage("New shipping address is required");

            When(x => x.NewShippingAddress is not null, () =>
            {
                RuleFor(x => x.NewShippingAddress.Street)
                    .NotEmpty()
                    .WithMessage("Street is required");

                RuleFor(x => x.NewShippingAddress.City)
                    .NotEmpty()
                    .WithMessage("City is required");

                RuleFor(x => x.NewShippingAddress.State)
                    .NotEmpty()
                    .WithMessage("State is required");

                RuleFor(x => x.NewShippingAddress.PostalCode)
                    .NotEmpty()
                    .WithMessage("Postal code is required");

                RuleFor(x => x.NewShippingAddress.Country)
                    .NotEmpty()
                    .WithMessage("Country is required");
            });
        }
    }
}
