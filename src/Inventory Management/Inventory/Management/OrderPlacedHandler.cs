using Messages.Contracts.Orders;
using Wolverine;

namespace Inventory.Management;

/// <summary>
/// Wolverine handler for OrderPlaced integration events from Orders BC.
/// Reacts to new orders by initiating inventory reservations for each line item.
/// </summary>
public static class OrderPlacedHandler
{
    /// <summary>
    /// Handles an OrderPlaced event by creating ReserveStock commands for each line item.
    /// Groups line items by SKU to aggregate quantities, then creates one reservation per SKU.
    /// Assumes all inventory is at a default warehouse for now (future: intelligent warehouse routing).
    /// </summary>
    /// <param name="event">The OrderPlaced integration event from Orders BC.</param>
    /// <returns>Outgoing messages containing ReserveStock commands.</returns>
    public static OutgoingMessages Handle(OrderPlaced @event)
    {
        const string defaultWarehouse = "WH-01"; // TODO: Implement warehouse routing logic

        var messages = new OutgoingMessages();

        // Group line items by SKU and sum quantities
        var itemsBySku = @event.LineItems
            .GroupBy(item => item.Sku)
            .Select(g => new { Sku = g.Key, Quantity = g.Sum(item => item.Quantity) });

        // Create a ReserveStock command for each SKU
        foreach (var item in itemsBySku)
        {
            var reservationId = Guid.CreateVersion7();
            var reserveStock = new ReserveStock(
                @event.OrderId,
                item.Sku,
                defaultWarehouse,
                reservationId,
                item.Quantity);

            messages.Add(reserveStock);
        }

        return messages;
    }
}
