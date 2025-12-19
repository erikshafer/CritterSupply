using FluentValidation;

namespace Fulfillment.Shipments;

/// <summary>
/// Command to confirm successful delivery of a shipment.
/// </summary>
public sealed record ConfirmDelivery(
    Guid ShipmentId,
    string? RecipientName = null)
{
    public class ConfirmDeliveryValidator : AbstractValidator<ConfirmDelivery>
    {
        public ConfirmDeliveryValidator()
        {
            RuleFor(x => x.ShipmentId).NotEmpty();
            RuleFor(x => x.RecipientName).MaximumLength(200);
        }
    }
}
