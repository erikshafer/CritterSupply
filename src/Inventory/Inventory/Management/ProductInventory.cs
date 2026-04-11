namespace Inventory.Management;

/// <summary>
/// Event-sourced aggregate representing inventory for a specific SKU at a warehouse.
/// Tracks available, reserved, committed, and picked quantities to prevent overselling.
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
    Dictionary<Guid, int> PickedAllocations,
    bool HasPendingBackorders,
    int QuarantinedQuantity,
    DateTimeOffset InitializedAt)
{
    public int ReservedQuantity => Reservations.Values.Sum();
    public int CommittedQuantity => CommittedAllocations.Values.Sum();
    public int PickedQuantity => PickedAllocations.Values.Sum();
    public int TotalOnHand => AvailableQuantity + ReservedQuantity + CommittedQuantity + PickedQuantity;

    public static ProductInventory Create(InventoryInitialized @event) =>
        new(InventoryStreamId.Compute(@event.Sku, @event.WarehouseId),
            @event.Sku,
            @event.WarehouseId,
            @event.InitialQuantity,
            new Dictionary<Guid, int>(),
            new Dictionary<Guid, int>(),
            new Dictionary<Guid, Guid>(),
            new Dictionary<Guid, int>(),
            false,
            0,
            @event.InitializedAt);

    /// <summary>
    /// Generates a deterministic GUID from SKU and WarehouseId using MD5.
    /// </summary>
    [Obsolete("Use InventoryStreamId.Compute() instead. MD5-based IDs are replaced by UUID v5 per ADR 0060.")]
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

    public ProductInventory Apply(InventoryAdjusted @event) =>
        this with { AvailableQuantity = AvailableQuantity + @event.AdjustmentQuantity };

    // ---------------------------------------------------------------------------
    // S2 — Physical pick/ship tracking, failure modes, physical operations
    // ---------------------------------------------------------------------------

    /// <summary>
    /// StockPicked: moves quantity from CommittedAllocations → PickedAllocations.
    /// TotalOnHand is preserved (item is still in the building).
    /// </summary>
    public ProductInventory Apply(StockPicked @event)
    {
        var newCommitted = new Dictionary<Guid, int>(CommittedAllocations);
        newCommitted.Remove(@event.ReservationId);

        var newPicked = new Dictionary<Guid, int>(PickedAllocations)
        {
            [@event.ReservationId] = @event.Quantity
        };

        return this with
        {
            CommittedAllocations = newCommitted,
            PickedAllocations = newPicked
        };
    }

    /// <summary>
    /// StockShipped: removes quantity from PickedAllocations. TotalOnHand decrements
    /// (stock has physically left the building).
    /// </summary>
    public ProductInventory Apply(StockShipped @event)
    {
        var newPicked = new Dictionary<Guid, int>(PickedAllocations);
        newPicked.Remove(@event.ReservationId);

        var newReservationOrderIds = new Dictionary<Guid, Guid>(ReservationOrderIds);
        newReservationOrderIds.Remove(@event.ReservationId);

        return this with
        {
            PickedAllocations = newPicked,
            ReservationOrderIds = newReservationOrderIds
        };
    }

    /// <summary>
    /// StockDiscrepancyFound: no state change on aggregate — serves as an audit/alert record.
    /// </summary>
    public ProductInventory Apply(StockDiscrepancyFound @event) => this;

    /// <summary>
    /// ReservationExpired: same logic as ReservationReleased — stock returns to available pool.
    /// </summary>
    public ProductInventory Apply(ReservationExpired @event)
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

    public ProductInventory Apply(BackorderRegistered @event) =>
        this with { HasPendingBackorders = true };

    public ProductInventory Apply(BackorderCleared @event) =>
        this with { HasPendingBackorders = false };

    public ProductInventory Apply(CycleCountInitiated @event) => this;
    public ProductInventory Apply(CycleCountCompleted @event) => this;

    public ProductInventory Apply(DamageRecorded @event) => this;
    public ProductInventory Apply(StockWrittenOff @event) => this;

    // ---------------------------------------------------------------------------
    // S3 — Transfer and quarantine tracking
    // ---------------------------------------------------------------------------

    /// <summary>
    /// StockTransferredOut: deducts available quantity when stock is allocated
    /// for an outgoing inter-warehouse transfer.
    /// </summary>
    public ProductInventory Apply(StockTransferredOut @event) =>
        this with { AvailableQuantity = AvailableQuantity - @event.Quantity };

    /// <summary>
    /// StockTransferredIn: adds quantity when stock arrives from an inbound transfer.
    /// </summary>
    public ProductInventory Apply(StockTransferredIn @event) =>
        this with { AvailableQuantity = AvailableQuantity + @event.Quantity };

    /// <summary>
    /// StockQuarantined: tracks quarantined quantity. Actual available decrement is via InventoryAdjusted.
    /// </summary>
    public ProductInventory Apply(StockQuarantined @event) =>
        this with { QuarantinedQuantity = QuarantinedQuantity + @event.Quantity };

    /// <summary>
    /// QuarantineReleased: restores quarantined quantity. Actual available increment is via InventoryAdjusted.
    /// </summary>
    public ProductInventory Apply(QuarantineReleased @event) =>
        this with { QuarantinedQuantity = QuarantinedQuantity - @event.Quantity };

    /// <summary>
    /// QuarantineDisposed: removes from quarantine permanently. Write-off via StockWrittenOff.
    /// </summary>
    public ProductInventory Apply(QuarantineDisposed @event) =>
        this with { QuarantinedQuantity = QuarantinedQuantity - @event.Quantity };

    /// <summary>
    /// ReplenishmentTriggered: audit event only — no state change on the aggregate.
    /// </summary>
    public ProductInventory Apply(ReplenishmentTriggered @event) => this;
}
