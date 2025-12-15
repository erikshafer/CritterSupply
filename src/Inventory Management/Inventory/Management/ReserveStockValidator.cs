using FluentValidation;

namespace Inventory.Management;

public sealed class ReserveStockValidator : AbstractValidator<ReserveStock>
{
    public ReserveStockValidator()
    {
        RuleFor(x => x.SKU).NotEmpty().MaximumLength(50);
        RuleFor(x => x.WarehouseId).NotEmpty().MaximumLength(50);
        RuleFor(x => x.ReservationId).NotEmpty();
        RuleFor(x => x.Quantity).GreaterThan(0);
    }
}
