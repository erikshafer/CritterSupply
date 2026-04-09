using Marten;
using Messages.Contracts.Fulfillment;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Http;
using IntegrationMessages = Messages.Contracts.Inventory;

namespace Inventory.Management;

/// <summary>
/// Handles StockReservationRequested integration messages from Fulfillment BC.
/// This is the new routing-aware reservation flow that replaces the legacy
/// OrderPlacedHandler's hardcoded WH-01 path.
///
/// Business logic is identical to ReserveStockHandler, but the trigger is an
/// integration message (Fulfillment → Inventory) rather than an internal command.
///
/// MIGRATION BRIDGE: Runs alongside OrderPlacedHandler during Phase 1.
/// See ADR 0060, Section 1 for the complete routing integration migration plan.
/// </summary>
public static class StockReservationRequestedHandler
{
    public static async Task<ProductInventory?> Load(
        StockReservationRequested message,
        IDocumentSession session,
        CancellationToken ct)
    {
        var inventoryId = InventoryStreamId.Compute(message.Sku, message.WarehouseId);
        return await session.LoadAsync<ProductInventory>(inventoryId, ct);
    }

    public static ProblemDetails Before(
        StockReservationRequested message,
        ProductInventory? inventory)
    {
        if (inventory is null)
            return new ProblemDetails
            {
                Detail = $"No inventory found for SKU {message.Sku} at warehouse {message.WarehouseId}",
                Status = 404
            };

        if (inventory.AvailableQuantity < message.Quantity)
            return new ProblemDetails
            {
                Detail = $"Insufficient stock for SKU {message.Sku}. Requested: {message.Quantity}, Available: {inventory.AvailableQuantity}",
                Status = 409
            };

        return WolverineContinue.NoProblems;
    }

    public static OutgoingMessages Handle(
        StockReservationRequested message,
        ProductInventory inventory,
        IDocumentSession session)
    {
        var reservedAt = DateTimeOffset.UtcNow;

        var domainEvent = new StockReserved(
            message.OrderId,
            message.ReservationId,
            message.Sku,
            message.WarehouseId,
            message.Quantity,
            reservedAt);

        session.Events.Append(inventory.Id, domainEvent);

        var outgoing = new OutgoingMessages();
        outgoing.Add(new IntegrationMessages.ReservationConfirmed(
            message.OrderId,
            inventory.Id,
            message.ReservationId,
            message.Sku,
            message.WarehouseId,
            message.Quantity,
            reservedAt));

        return outgoing;
    }
}
