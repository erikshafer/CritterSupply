using System.Collections.Immutable;
using System.Security.Cryptography;

namespace Fulfillment.WorkOrders;

/// <summary>
/// Event-sourced aggregate representing warehouse operations for a shipment.
/// Owns the lifecycle from WorkOrderCreated through PackingCompleted.
/// Stream ID is UUID v5 derived from (ShipmentId, FulfillmentCenterId).
/// </summary>
public sealed record WorkOrder(
    Guid Id,
    Guid ShipmentId,
    string FulfillmentCenterId,
    IReadOnlyList<WorkOrderLineItem> LineItems,
    WorkOrderStatus Status,
    string? AssignedPicker,
    IReadOnlyDictionary<string, int> PickedQuantities,
    IReadOnlyDictionary<string, int> VerifiedQuantities,
    decimal BillableWeightLbs,
    string? CartonSize,
    DateTimeOffset CreatedAt,
    DateTimeOffset? WaveReleasedAt,
    DateTimeOffset? PickListAssignedAt,
    DateTimeOffset? PickStartedAt,
    DateTimeOffset? PickCompletedAt,
    DateTimeOffset? PackingStartedAt,
    DateTimeOffset? PackingCompletedAt)
{
    /// <summary>
    /// Creates a deterministic UUID v5 stream ID from (ShipmentId, FulfillmentCenterId).
    /// If a shipment is rerouted, the original WorkOrder stream stays closed;
    /// a new WorkOrder stream is created for the new FC.
    /// </summary>
    public static Guid StreamId(Guid shipmentId, string fulfillmentCenterId)
    {
        // Use the Fulfillment BC namespace UUID for work order streams
        var namespaceId = new Guid("a1b2c3d4-e5f6-7890-abcd-ef1234567890");
        var inputBytes = shipmentId.ToByteArray()
            .Concat(System.Text.Encoding.UTF8.GetBytes(fulfillmentCenterId))
            .ToArray();
        var namespaceBytes = namespaceId.ToByteArray();

        using var sha1 = SHA1.Create();
        var combined = namespaceBytes.Concat(inputBytes).ToArray();
        var hash = sha1.ComputeHash(combined);

        hash[6] = (byte)((hash[6] & 0x0F) | 0x50); // Version 5
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80); // Variant

        return new Guid(hash.Take(16).ToArray());
    }

    /// <summary>
    /// Whether all items in the work order have been picked.
    /// </summary>
    public bool AllItemsPicked =>
        LineItems.All(item =>
            PickedQuantities.TryGetValue(item.Sku, out var picked) && picked >= item.Quantity);

    /// <summary>
    /// Whether all items in the work order have been verified at the pack station.
    /// </summary>
    public bool AllItemsVerified =>
        LineItems.All(item =>
            VerifiedQuantities.TryGetValue(item.Sku, out var verified) && verified >= item.Quantity);

    public static WorkOrder Create(WorkOrderCreated @event) =>
        new(Guid.Empty,
            @event.ShipmentId,
            @event.FulfillmentCenterId,
            @event.LineItems,
            WorkOrderStatus.Created,
            null,
            ImmutableDictionary<string, int>.Empty,
            ImmutableDictionary<string, int>.Empty,
            0m,
            null,
            @event.CreatedAt,
            null, null, null, null, null, null);

    public WorkOrder Apply(WaveReleased @event) =>
        this with
        {
            Status = WorkOrderStatus.WaveReleased,
            WaveReleasedAt = @event.ReleasedAt
        };

    public WorkOrder Apply(PickListAssigned @event) =>
        this with
        {
            Status = WorkOrderStatus.PickListAssigned,
            AssignedPicker = @event.PickerId,
            PickListAssignedAt = @event.AssignedAt
        };

    public WorkOrder Apply(PickStarted @event) =>
        this with
        {
            Status = WorkOrderStatus.PickStarted,
            PickStartedAt = @event.StartedAt
        };

    public WorkOrder Apply(ItemPicked @event)
    {
        var newPicked = new Dictionary<string, int>(PickedQuantities);
        newPicked[@event.Sku] = newPicked.GetValueOrDefault(@event.Sku) + @event.Quantity;
        return this with { PickedQuantities = newPicked.ToImmutableDictionary() };
    }

    public WorkOrder Apply(PickCompleted @event) =>
        this with
        {
            Status = WorkOrderStatus.PickCompleted,
            PickCompletedAt = @event.CompletedAt
        };

    public WorkOrder Apply(PackingStarted @event) =>
        this with
        {
            Status = WorkOrderStatus.PackingStarted,
            PackingStartedAt = @event.StartedAt
        };

    public WorkOrder Apply(ItemVerifiedAtPack @event)
    {
        var newVerified = new Dictionary<string, int>(VerifiedQuantities);
        newVerified[@event.Sku] = newVerified.GetValueOrDefault(@event.Sku) + @event.Quantity;
        return this with { VerifiedQuantities = newVerified.ToImmutableDictionary() };
    }

    public WorkOrder Apply(DIMWeightCalculated @event) =>
        this with { BillableWeightLbs = Math.Max(@event.WeightLbs, @event.DimWeightLbs) };

    public WorkOrder Apply(CartonSelected @event) =>
        this with { CartonSize = @event.CartonSize };

    public WorkOrder Apply(PackingCompleted @event) =>
        this with
        {
            Status = WorkOrderStatus.PackingCompleted,
            BillableWeightLbs = @event.BillableWeightLbs,
            CartonSize = @event.CartonSize,
            PackingCompletedAt = @event.CompletedAt
        };
}
