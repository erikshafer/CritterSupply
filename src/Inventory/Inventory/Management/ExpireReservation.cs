using Marten;
using Wolverine;
using IntegrationMessages = Messages.Contracts.Inventory;

namespace Inventory.Management;

/// <summary>
/// Command to expire a reservation after a timeout period.
/// Scheduled as a delayed message when a reservation is created.
/// </summary>
public sealed record ExpireReservation(Guid ReservationId, Guid InventoryId);

/// <summary>
/// Handles reservation expiry after a scheduled timeout.
/// Idempotent: if the reservation has already been committed or released, this is a no-op.
/// </summary>
public static class ExpireReservationHandler
{
    /// <summary>
    /// Reservation expiry timeout — 30 minutes from reservation creation.
    /// </summary>
    public static readonly TimeSpan ExpiryTimeout = TimeSpan.FromMinutes(30);

    public static async Task<ProductInventory?> Load(
        ExpireReservation command,
        IDocumentSession session,
        CancellationToken ct)
    {
        return await session.LoadAsync<ProductInventory>(command.InventoryId, ct);
    }

    public static OutgoingMessages Handle(
        ExpireReservation command,
        ProductInventory? inventory,
        IDocumentSession session)
    {
        var outgoing = new OutgoingMessages();

        if (inventory is null)
            return outgoing;

        // Idempotency: if already committed or released, no-op
        if (!inventory.Reservations.ContainsKey(command.ReservationId))
            return outgoing;

        var quantity = inventory.Reservations[command.ReservationId];
        var orderId = inventory.ReservationOrderIds.GetValueOrDefault(command.ReservationId);

        session.Events.Append(inventory.Id,
            new ReservationExpired(
                command.ReservationId,
                inventory.Sku,
                inventory.WarehouseId,
                quantity,
                "Reservation expired after timeout",
                DateTimeOffset.UtcNow));

        outgoing.Add(new IntegrationMessages.ReservationReleased(
            orderId,
            inventory.Id,
            command.ReservationId,
            inventory.Sku,
            inventory.WarehouseId,
            quantity,
            "Expired",
            DateTimeOffset.UtcNow));

        return outgoing;
    }
}
