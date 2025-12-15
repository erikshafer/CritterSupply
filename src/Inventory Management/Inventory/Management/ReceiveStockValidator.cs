using FluentValidation;

namespace Inventory.Management;

public sealed class ReceiveStockValidator : AbstractValidator<ReceiveStock>
{
    public ReceiveStockValidator()
    {
        RuleFor(x => x.InventoryId).NotEmpty();
        RuleFor(x => x.Quantity).GreaterThan(0);
        RuleFor(x => x.Source).NotEmpty().MaximumLength(100);
    }
}
