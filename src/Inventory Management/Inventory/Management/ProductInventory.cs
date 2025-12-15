using IntegrationMessages = Messages.Contracts.Inventory;

namespace Inventory.Management;

/// <summary>
/// Event-sourced aggregate representing inventory for a specific SKU at a warehouse.
/// Tracks available, reserved, and committed quantities to prevent overselling.
/// </summary>
public sealed record ProductInventory(
    Guid Id,
    string SKU,
    string WarehouseId,
    int AvailableQuantity,
    Dictionary<Guid, int> Reservations,
    Dictionary<Guid, int> CommittedAllocations,
    DateTimeOffset InitializedAt)
{
    /// <summary>
    /// Collection of uncommitted events for this aggregate.
    /// </summary>
    internal List<object> PendingEvents { get; } = [];

    /// <summary>
    /// Gets the total quantity reserved (soft holds).
    /// </summary>
    public int ReservedQuantity => Reservations.Values.Sum();

    /// <summary>
    /// Gets the total quantity committed (hard allocations).
    /// </summary>
    public int CommittedQuantity => CommittedAllocations.Values.Sum();

    /// <summary>
    /// Gets the total quantity on hand (available + reserved + committed).
    /// </summary>
    public int TotalOnHand => AvailableQuantity + ReservedQuantity + CommittedQuantity;

    /// <summary>
    /// Creates a new ProductInventory from an InitializeInventory command.
    /// </summary>
    public static ProductInventory Create(string sku, string warehouseId, int initialQuantity)
    {
        var inventoryId = Guid.CreateVersion7();
        var initializedAt = DateTimeOffset.UtcNow;

        var inventory = new ProductInventory(
            inventoryId,
            sku,
            warehouseId,
            initialQuantity,
            new Dictionary<Guid, int>(),
            new Dictionary<Guid, int>(),
            initializedAt);

        inventory.PendingEvents.Add(new InventoryInitialized(
            sku,
            warehouseId,
            initialQuantity,
            initializedAt));

        return inventory;
    }

    /// <summary>
    /// Reserves stock for an order (soft hold during checkout).
    /// Returns updated inventory and integration message.
    /// </summary>
    public (ProductInventory, StockReserved, IntegrationMessages.ReservationConfirmed) Reserve(
        Guid reservationId,
        int quantity)
    {
        var reservedAt = DateTimeOffset.UtcNow;

        var newReservations = new Dictionary<Guid, int>(Reservations)
        {
            [reservationId] = quantity
        };

        var updated = this with
        {
            AvailableQuantity = AvailableQuantity - quantity,
            Reservations = newReservations
        };

        var domainEvent = new StockReserved(reservationId, quantity, reservedAt);
        updated.PendingEvents.AddRange(PendingEvents);
        updated.PendingEvents.Add(domainEvent);

        var integrationMessage = new IntegrationMessages.ReservationConfirmed(
            Id,
            reservationId,
            SKU,
            WarehouseId,
            quantity,
            reservedAt);

        return (updated, domainEvent, integrationMessage);
    }

    /// <summary>
    /// Commits a reservation, converting soft hold to hard allocation.
    /// Returns updated inventory and integration message.
    /// </summary>
    public (ProductInventory, ReservationCommitted, IntegrationMessages.ReservationCommitted) Commit(
        Guid reservationId)
    {
        var committedAt = DateTimeOffset.UtcNow;

        if (!Reservations.TryGetValue(reservationId, out var quantity))
        {
            throw new InvalidOperationException($"Reservation {reservationId} not found");
        }

        var newReservations = new Dictionary<Guid, int>(Reservations);
        newReservations.Remove(reservationId);

        var newCommitted = new Dictionary<Guid, int>(CommittedAllocations)
        {
            [reservationId] = quantity
        };

        var updated = this with
        {
            Reservations = newReservations,
            CommittedAllocations = newCommitted
        };

        var domainEvent = new ReservationCommitted(reservationId, committedAt);
        updated.PendingEvents.AddRange(PendingEvents);
        updated.PendingEvents.Add(domainEvent);

        var integrationMessage = new IntegrationMessages.ReservationCommitted(
            Id,
            reservationId,
            SKU,
            WarehouseId,
            quantity,
            committedAt);

        return (updated, domainEvent, integrationMessage);
    }

    /// <summary>
    /// Releases a reservation, returning stock to available pool.
    /// Returns updated inventory and integration message.
    /// </summary>
    public (ProductInventory, ReservationReleased, IntegrationMessages.ReservationReleased) Release(
        Guid reservationId,
        string reason)
    {
        var releasedAt = DateTimeOffset.UtcNow;

        if (!Reservations.TryGetValue(reservationId, out var quantity))
        {
            throw new InvalidOperationException($"Reservation {reservationId} not found");
        }

        var newReservations = new Dictionary<Guid, int>(Reservations);
        newReservations.Remove(reservationId);

        var updated = this with
        {
            AvailableQuantity = AvailableQuantity + quantity,
            Reservations = newReservations
        };

        var domainEvent = new ReservationReleased(reservationId, quantity, reason, releasedAt);
        updated.PendingEvents.AddRange(PendingEvents);
        updated.PendingEvents.Add(domainEvent);

        var integrationMessage = new IntegrationMessages.ReservationReleased(
            Id,
            reservationId,
            SKU,
            WarehouseId,
            quantity,
            reason,
            releasedAt);

        return (updated, domainEvent, integrationMessage);
    }

    /// <summary>
    /// Receives new stock from supplier or warehouse transfer.
    /// Returns updated inventory.
    /// </summary>
    public (ProductInventory, StockReceived) ReceiveStock(int quantity, string source)
    {
        var receivedAt = DateTimeOffset.UtcNow;

        var updated = this with
        {
            AvailableQuantity = AvailableQuantity + quantity
        };

        var domainEvent = new StockReceived(quantity, source, receivedAt);
        updated.PendingEvents.AddRange(PendingEvents);
        updated.PendingEvents.Add(domainEvent);

        return (updated, domainEvent);
    }

    /// <summary>
    /// Restocks inventory from returns.
    /// Returns updated inventory.
    /// </summary>
    public (ProductInventory, StockRestocked) Restock(Guid returnId, int quantity)
    {
        var restockedAt = DateTimeOffset.UtcNow;

        var updated = this with
        {
            AvailableQuantity = AvailableQuantity + quantity
        };

        var domainEvent = new StockRestocked(returnId, quantity, restockedAt);
        updated.PendingEvents.AddRange(PendingEvents);
        updated.PendingEvents.Add(domainEvent);

        return (updated, domainEvent);
    }

    #region Marten Event Sourcing

    /// <summary>
    /// Creates a ProductInventory from an InventoryInitialized event (Marten event sourcing).
    /// Used by Marten to reconstruct aggregate state from events.
    /// </summary>
    public static ProductInventory Create(InventoryInitialized @event) =>
        new(CombinedGuid(@event.SKU, @event.WarehouseId),
            @event.SKU,
            @event.WarehouseId,
            @event.InitialQuantity,
            new Dictionary<Guid, int>(),
            new Dictionary<Guid, int>(),
            @event.InitializedAt);

    /// <summary>
    /// Creates a deterministic GUID from SKU and WarehouseId combination.
    /// </summary>
    private static Guid CombinedGuid(string sku, string warehouseId)
    {
        var combined = $"{sku}:{warehouseId}";
        using var md5 = System.Security.Cryptography.MD5.Create();
        var hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(combined));
        return new Guid(hash);
    }

    /// <summary>
    /// Applies a StockReserved event to update state (Marten event sourcing).
    /// </summary>
    public ProductInventory Apply(StockReserved @event)
    {
        var newReservations = new Dictionary<Guid, int>(Reservations)
        {
            [@event.ReservationId] = @event.Quantity
        };

        return this with
        {
            AvailableQuantity = AvailableQuantity - @event.Quantity,
            Reservations = newReservations
        };
    }

    /// <summary>
    /// Applies a ReservationCommitted event to update state (Marten event sourcing).
    /// </summary>
    public ProductInventory Apply(ReservationCommitted @event)
    {
        if (!Reservations.TryGetValue(@event.ReservationId, out var quantity))
        {
            return this; // Event replay protection
        }

        var newReservations = new Dictionary<Guid, int>(Reservations);
        newReservations.Remove(@event.ReservationId);

        var newCommitted = new Dictionary<Guid, int>(CommittedAllocations)
        {
            [@event.ReservationId] = quantity
        };

        return this with
        {
            Reservations = newReservations,
            CommittedAllocations = newCommitted
        };
    }

    /// <summary>
    /// Applies a ReservationReleased event to update state (Marten event sourcing).
    /// </summary>
    public ProductInventory Apply(ReservationReleased @event)
    {
        if (!Reservations.TryGetValue(@event.ReservationId, out var quantity))
        {
            return this; // Event replay protection
        }

        var newReservations = new Dictionary<Guid, int>(Reservations);
        newReservations.Remove(@event.ReservationId);

        return this with
        {
            AvailableQuantity = AvailableQuantity + quantity,
            Reservations = newReservations
        };
    }

    /// <summary>
    /// Applies a StockReceived event to update state (Marten event sourcing).
    /// </summary>
    public ProductInventory Apply(StockReceived @event) =>
        this with
        {
            AvailableQuantity = AvailableQuantity + @event.Quantity
        };

    /// <summary>
    /// Applies a StockRestocked event to update state (Marten event sourcing).
    /// </summary>
    public ProductInventory Apply(StockRestocked @event) =>
        this with
        {
            AvailableQuantity = AvailableQuantity + @event.Quantity
        };

    #endregion
}
