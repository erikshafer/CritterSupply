using FluentValidation;

namespace Fulfillment.Shipments;

/// <summary>
/// Command to assign a warehouse to fulfill a shipment.
/// </summary>
public sealed record AssignWarehouse(
    Guid ShipmentId,
    string WarehouseId)
{
    public class AssignWarehouseValidator : AbstractValidator<AssignWarehouse>
    {
        public AssignWarehouseValidator()
        {
            RuleFor(x => x.ShipmentId).NotEmpty();
            RuleFor(x => x.WarehouseId).NotEmpty().MaximumLength(50);
        }
    }
}
