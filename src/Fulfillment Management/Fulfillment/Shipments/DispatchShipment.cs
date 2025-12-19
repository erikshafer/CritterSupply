using FluentValidation;

namespace Fulfillment.Shipments;

/// <summary>
/// Command to dispatch a shipment with carrier and tracking information.
/// </summary>
public sealed record DispatchShipment(
    Guid ShipmentId,
    string Carrier,
    string TrackingNumber)
{
    public class DispatchShipmentValidator : AbstractValidator<DispatchShipment>
    {
        public DispatchShipmentValidator()
        {
            RuleFor(x => x.ShipmentId).NotEmpty();
            RuleFor(x => x.Carrier).NotEmpty().MaximumLength(100);
            RuleFor(x => x.TrackingNumber).NotEmpty().MaximumLength(100);
        }
    }
}
