using FluentValidation;
using Wolverine.Marten;

namespace Inventory.Management;

public sealed record InitializeInventory(
    string Sku,
    string WarehouseId,
    int InitialQuantity)
{
    public class InitializeInventoryValidator : AbstractValidator<InitializeInventory>
    {
        public InitializeInventoryValidator()
        {
            RuleFor(x => x.Sku).NotEmpty().MaximumLength(50);
            RuleFor(x => x.WarehouseId).NotEmpty().MaximumLength(50);
            RuleFor(x => x.InitialQuantity).GreaterThanOrEqualTo(0);
        }
    }
}

public static class InitializeInventoryHandler
{
    public static IStartStream Handle(InitializeInventory command)
    {
        var @event = new InventoryInitialized(
            command.Sku,
            command.WarehouseId,
            command.InitialQuantity,
            DateTimeOffset.UtcNow);

        var inventoryId = ProductInventory.CombinedGuid(command.Sku, command.WarehouseId);

        return MartenOps.StartStream<ProductInventory>(inventoryId, @event);
    }
}
