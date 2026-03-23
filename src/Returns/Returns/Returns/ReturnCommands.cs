namespace Returns.Returns;

public sealed record StartInspection(Guid ReturnId, string InspectorId);

public sealed record ExpireReturn(Guid ReturnId);

// ---------------------------------------------------------------------------
// Exchange-specific commands
// ---------------------------------------------------------------------------

/// <summary>
/// CS agent approves an exchange request after verifying stock availability.
/// </summary>
public sealed record ApproveExchange(Guid ReturnId);

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
