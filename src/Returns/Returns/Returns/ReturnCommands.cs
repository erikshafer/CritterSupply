namespace Returns.Returns;

// ---------------------------------------------------------------------------
// Exchange-specific commands
// ---------------------------------------------------------------------------

/// <summary>
/// Warehouse ships the replacement item after inspection passes.
/// </summary>
public sealed record ShipReplacementItem(
    Guid ReturnId,
    string ShipmentId,
    string TrackingNumber);
