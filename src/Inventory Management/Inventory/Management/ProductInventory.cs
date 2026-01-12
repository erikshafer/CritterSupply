namespace Inventory.Management;

/// <summary>
/// Event-sourced aggregate representing inventory for a specific SKU at a warehouse.
/// Tracks available, reserved, and committed quantities to prevent overselling.
/// Write-only model: contains only state and Apply() methods for event application.
/// Business logic resides in handlers using the Decider pattern.
/// </summary>
public sealed record ProductInventory(
    Guid Id,
    string Sku,
    string WarehouseId,
    int AvailableQuantity,
    Dictionary<Guid, int> Reservations,
    Dictionary<Guid, int> CommittedAllocations,
    Dictionary<Guid, Guid> ReservationOrderIds,
    DateTimeOffset InitializedAt)
{
    public int ReservedQuantity => Reservations.Values.Sum();
    public int CommittedQuantity => CommittedAllocations.Values.Sum();
    public int TotalOnHand => AvailableQuantity + ReservedQuantity + CommittedQuantity;

    public static ProductInventory Create(InventoryInitialized @event) =>
        new(CombinedGuid(@event.Sku, @event.WarehouseId),
            @event.Sku,
            @event.WarehouseId,
            @event.InitialQuantity,
            new Dictionary<Guid, int>(),
            new Dictionary<Guid, int>(),
            new Dictionary<Guid, Guid>(),
            @event.InitializedAt);

    public static Guid CombinedGuid(string sku, string warehouseId)
    {
        var combined = $"{sku}:{warehouseId}";
        using var md5 = System.Security.Cryptography.MD5.Create();
        var hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(combined));
        return new Guid(hash);
    }

    public ProductInventory Apply(StockReserved @event)
    {
        var newReservations = new Dictionary<Guid, int>(Reservations)
        {
            [@event.ReservationId] = @event.Quantity
        };

        var newReservationOrderIds = new Dictionary<Guid, Guid>(ReservationOrderIds)
        {
            [@event.ReservationId] = @event.OrderId
        };

        return this with
        {
            AvailableQuantity = AvailableQuantity - @event.Quantity,
            Reservations = newReservations,
            ReservationOrderIds = newReservationOrderIds
        };
    }

    public ProductInventory Apply(ReservationCommitted @event)
    {
        if (!Reservations.TryGetValue(@event.ReservationId, out var quantity))
            return this;

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

    public ProductInventory Apply(ReservationReleased @event)
    {
        if (!Reservations.TryGetValue(@event.ReservationId, out var quantity))
            return this;

        var newReservations = new Dictionary<Guid, int>(Reservations);
        newReservations.Remove(@event.ReservationId);

        return this with
        {
            AvailableQuantity = AvailableQuantity + quantity,
            Reservations = newReservations
        };
    }

    public ProductInventory Apply(StockReceived @event) =>
        this with { AvailableQuantity = AvailableQuantity + @event.Quantity };

    public ProductInventory Apply(StockRestocked @event) =>
        this with { AvailableQuantity = AvailableQuantity + @event.Quantity };
}
