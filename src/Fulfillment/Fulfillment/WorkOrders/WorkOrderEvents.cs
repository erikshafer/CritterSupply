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

// --- Failure Mode Events (P1) ---

/// <summary>Domain event when an item is not found at its expected bin during picking.</summary>
public sealed record ItemNotFoundAtBin(
    string Sku,
    string BinLocation,
    DateTimeOffset DetectedAt);

/// <summary>Domain event when a short pick is detected — expected quantity not available.</summary>
public sealed record ShortPickDetected(
    string Sku,
    int ExpectedQuantity,
    int ShortageQuantity,
    DateTimeOffset DetectedAt);

/// <summary>Domain event when picking resumes after a short pick, using an alternative bin.</summary>
public sealed record PickResumed(
    string Sku,
    string AlternativeBinLocation,
    DateTimeOffset ResumedAt);

/// <summary>Domain event when a pick exception is raised and the work order is closed.</summary>
public sealed record PickExceptionRaised(
    string Reason,
    DateTimeOffset RaisedAt);

/// <summary>Domain event when a wrong item is scanned at the pack station.</summary>
public sealed record WrongItemScannedAtPack(
    string ExpectedSku,
    string ScannedSku,
    DateTimeOffset DetectedAt);

/// <summary>Domain event when a pack discrepancy is detected (wrong item or weight mismatch).</summary>
public sealed record PackDiscrepancyDetected(
    string DiscrepancyType,
    string Description,
    DateTimeOffset DetectedAt);

/// <summary>Domain event when an SLA escalation threshold is reached.</summary>
public sealed record SLAEscalationRaised(
    int Threshold,
    TimeSpan ElapsedTime,
    TimeSpan SlaWindow,
    DateTimeOffset RaisedAt);

/// <summary>Domain event when the SLA window is fully breached.</summary>
public sealed record SLABreached(
    TimeSpan ElapsedTime,
    TimeSpan SlaWindow,
    DateTimeOffset BreachedAt);
