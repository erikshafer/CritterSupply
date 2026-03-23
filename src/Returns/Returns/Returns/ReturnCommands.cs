namespace Returns.Returns;

// ---------------------------------------------------------------------------
// Exchange-specific commands
// ---------------------------------------------------------------------------

/// <summary>
/// CS agent denies an exchange request (out of stock, outside window, or replacement too expensive).
/// </summary>
public sealed record DenyExchange(Guid ReturnId, string Reason, string Message);

/// <summary>
/// Warehouse ships the replacement item after inspection passes.
/// </summary>
public sealed record ShipReplacementItem(
    Guid ReturnId,
    string ShipmentId,
    string TrackingNumber);
