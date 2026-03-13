namespace Returns.Returns;

/// <summary>
/// Request from the command to capture the exchange request details
/// </summary>
public sealed record RequestReturnExchangeRequest(
    string ReplacementSku,
    int ReplacementQuantity,
    decimal ReplacementUnitPrice);

public sealed record RequestReturn(
    Guid OrderId,
    Guid CustomerId,
    IReadOnlyList<RequestReturnItem> Items,
    RequestReturnExchangeRequest? ExchangeRequest = null);

public sealed record RequestReturnItem(
    string Sku,
    string ProductName,
    int Quantity,
    decimal UnitPrice,
    ReturnReason Reason,
    string? Explanation = null);

public sealed record ApproveReturn(Guid ReturnId);

public sealed record DenyReturn(Guid ReturnId, string Reason, string Message);

public sealed record ReceiveReturn(Guid ReturnId);

public sealed record StartInspection(Guid ReturnId, string InspectorId);

public sealed record SubmitInspection(
    Guid ReturnId,
    IReadOnlyList<InspectionLineResult> Results);

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
