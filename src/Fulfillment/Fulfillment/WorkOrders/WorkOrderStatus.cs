namespace Fulfillment.WorkOrders;

/// <summary>
/// Represents the lifecycle status of a work order in the warehouse.
/// </summary>
public enum WorkOrderStatus
{
    /// <summary>Work order created, awaiting wave release.</summary>
    Created,

    /// <summary>Included in a pick wave, pick list generated.</summary>
    WaveReleased,

    /// <summary>Pick list assigned to a specific picker.</summary>
    PickListAssigned,

    /// <summary>Picker has started picking items.</summary>
    PickStarted,

    /// <summary>Short pick detected — item not found at bin, awaiting resolution.</summary>
    ShortPickPending,

    /// <summary>All items successfully picked.</summary>
    PickCompleted,

    /// <summary>Items arrived at pack station, packing in progress.</summary>
    PackingStarted,

    /// <summary>Pack station discrepancy detected — wrong item or weight mismatch.</summary>
    PackDiscrepancyPending,

    /// <summary>All items verified, carton sealed, ready for labeling.</summary>
    PackingCompleted,

    /// <summary>Pick exception raised — work order closed (rerouted or backordered).</summary>
    PickExceptionClosed
}
