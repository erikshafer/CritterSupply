using FluentValidation;

namespace Inventory.Management;

public sealed class InitializeInventoryValidator : AbstractValidator<InitializeInventory>
{
    public InitializeInventoryValidator()
    {
        RuleFor(x => x.SKU).NotEmpty().MaximumLength(50);
        RuleFor(x => x.WarehouseId).NotEmpty().MaximumLength(50);
        RuleFor(x => x.InitialQuantity).GreaterThanOrEqualTo(0);
    }
}
