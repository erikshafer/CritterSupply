namespace Fulfillment.WorkOrders;

/// <summary>Domain event when a work order is created at a fulfillment center.</summary>
public sealed record WorkOrderCreated(
    Guid WorkOrderId,
    Guid ShipmentId,
    string FulfillmentCenterId,
    IReadOnlyList<WorkOrderLineItem> LineItems,
    DateTimeOffset CreatedAt);

/// <summary>Domain event when a work order is included in a pick wave.</summary>
public sealed record WaveReleased(
    string WaveId,
    DateTimeOffset ReleasedAt);

/// <summary>Domain event when a pick list is created for the wave.</summary>
public sealed record PickListCreated(
    string PickListId,
    DateTimeOffset CreatedAt);

/// <summary>Domain event when a pick list is assigned to a specific picker.</summary>
public sealed record PickListAssigned(
    string PickerId,
    DateTimeOffset AssignedAt);

/// <summary>Domain event when a picker begins picking at the first bin.</summary>
public sealed record PickStarted(
    DateTimeOffset StartedAt);

/// <summary>Domain event when an individual SKU is scanned and picked at a bin.</summary>
public sealed record ItemPicked(
    string Sku,
    int Quantity,
    string BinLocation,
    string PickedBy,
    DateTimeOffset PickedAt);

/// <summary>Domain event when all items for the work order have been picked.</summary>
public sealed record PickCompleted(
    DateTimeOffset CompletedAt);

/// <summary>Domain event when items arrive at the pack station.</summary>
public sealed record PackingStarted(
    DateTimeOffset StartedAt);

/// <summary>Domain event when an item is verified at the pack station via SVP scan.</summary>
public sealed record ItemVerifiedAtPack(
    string Sku,
    int Quantity,
    DateTimeOffset VerifiedAt);

/// <summary>Domain event when dimensional weight is calculated for the carton.</summary>
public sealed record DIMWeightCalculated(
    decimal WeightLbs,
    decimal LengthIn,
    decimal WidthIn,
    decimal HeightIn,
    decimal DimWeightLbs,
    DateTimeOffset CalculatedAt);

/// <summary>Domain event when the system selects the carton size.</summary>
public sealed record CartonSelected(
    string CartonSize,
    DateTimeOffset SelectedAt);

/// <summary>Domain event when all items are verified, carton is sealed.</summary>
public sealed record PackingCompleted(
    decimal BillableWeightLbs,
    string CartonSize,
    DateTimeOffset CompletedAt);
