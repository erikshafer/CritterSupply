namespace Returns.ReturnProcessing;

public sealed record ReturnLineItem(
    string Sku,
    string ProductName,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal,
    ReturnReason Reason,
    string? Explanation = null);

/// <summary>
/// Exchange request details for replacement items.
/// Phase 1: Same-SKU, no upcharge. Phase 2: Cross-product with price difference handling.
/// </summary>
public sealed record ExchangeRequest(
    string ReplacementSku,
    int ReplacementQuantity,
    decimal ReplacementUnitPrice);

public sealed record ReturnRequested(
    Guid ReturnId,
    Guid OrderId,
    Guid CustomerId,
    IReadOnlyList<ReturnLineItem> Items,
    ReturnType Type,
    ExchangeRequest? ExchangeRequest,
    DateTimeOffset RequestedAt);

public sealed record ReturnApproved(
    Guid ReturnId,
    decimal EstimatedRefundAmount,
    decimal RestockingFeeAmount,
    DateTimeOffset ShipByDeadline,
    DateTimeOffset ApprovedAt);

public sealed record ReturnDenied(
    Guid ReturnId,
    string Reason,
    string Message,
    DateTimeOffset DeniedAt);

public sealed record ReturnReceived(
    Guid ReturnId,
    DateTimeOffset ReceivedAt);

public sealed record InspectionStarted(
    Guid ReturnId,
    string InspectorId,
    DateTimeOffset StartedAt);

public sealed record InspectionLineResult(
    string Sku,
    int Quantity,
    ItemCondition Condition,
    string? ConditionNotes,
    bool IsRestockable,
    DispositionDecision Disposition,
    string? WarehouseLocation);

public sealed record InspectionPassed(
    Guid ReturnId,
    IReadOnlyList<InspectionLineResult> Results,
    decimal FinalRefundAmount,
    decimal RestockingFeeAmount,
    DateTimeOffset CompletedAt);

public sealed record InspectionFailed(
    Guid ReturnId,
    IReadOnlyList<InspectionLineResult> Results,
    string FailureReason,
    DateTimeOffset CompletedAt);

/// <summary>
/// Some items passed inspection and some failed. The return completes
/// with a partial refund for passed items. Failed items get their own disposition.
/// </summary>
public sealed record InspectionMixed(
    Guid ReturnId,
    IReadOnlyList<InspectionLineResult> PassedItems,
    IReadOnlyList<InspectionLineResult> FailedItems,
    decimal FinalRefundAmount,
    decimal RestockingFeeAmount,
    DateTimeOffset CompletedAt);

public sealed record ReturnExpired(
    Guid ReturnId,
    DateTimeOffset ExpiredAt);

// ---------------------------------------------------------------------------
// Exchange-specific domain events
// ---------------------------------------------------------------------------

/// <summary>
/// Exchange approved after stock availability check.
/// Carries estimated refund for price difference (if replacement is cheaper).
/// </summary>
public sealed record ExchangeApproved(
    Guid ReturnId,
    decimal PriceDifference,
    DateTimeOffset ShipByDeadline,
    DateTimeOffset ApprovedAt);

/// <summary>
/// Exchange denied (out of stock, outside window, etc.).
/// </summary>
public sealed record ExchangeDenied(
    Guid ReturnId,
    string Reason,
    string Message,
    DateTimeOffset DeniedAt);

/// <summary>
/// Published when the replacement item ships after inspection passes.
/// </summary>
public sealed record ExchangeReplacementShipped(
    Guid ReturnId,
    string ShipmentId,
    string TrackingNumber,
    DateTimeOffset ShippedAt);

/// <summary>
/// Exchange completed successfully — original item inspected, replacement shipped.
/// Carries final price difference refund (if any).
/// </summary>
public sealed record ExchangeCompleted(
    Guid ReturnId,
    decimal? PriceDifferenceRefund,
    DateTimeOffset CompletedAt);

/// <summary>
/// Exchange rejected due to inspection failure. No replacement shipped, no refund issued.
/// </summary>
public sealed record ExchangeRejected(
    Guid ReturnId,
    string FailureReason,
    DateTimeOffset RejectedAt);

// ---------------------------------------------------------------------------
// Cross-product exchange domain events (Phase 2)
// ---------------------------------------------------------------------------

/// <summary>
/// Cross-product exchange requested — different SKU replacement.
/// Extends the standard exchange flow with cross-product capability.
/// </summary>
public sealed record CrossProductExchangeRequested(
    Guid ReturnId,
    string OriginalSku,
    string ReplacementSku,
    decimal OriginalUnitPrice,
    decimal ReplacementUnitPrice,
    int Quantity,
    DateTimeOffset RequestedAt);

/// <summary>
/// Price difference calculated between original and replacement items.
/// Positive difference = customer gets refund, negative = customer owes additional payment.
/// </summary>
public sealed record ExchangePriceDifferenceCalculated(
    Guid ReturnId,
    decimal OriginalTotal,
    decimal ReplacementTotal,
    decimal PriceDifference,
    DateTimeOffset CalculatedAt);

/// <summary>
/// Additional payment required because replacement costs more than original.
/// </summary>
public sealed record ExchangeAdditionalPaymentRequired(
    Guid ReturnId,
    decimal AmountDue,
    DateTimeOffset RequiredAt);

/// <summary>
/// Customer paid the additional amount for a more expensive replacement.
/// </summary>
public sealed record ExchangeAdditionalPaymentCaptured(
    Guid ReturnId,
    decimal AmountCaptured,
    string PaymentReference,
    DateTimeOffset CapturedAt);

/// <summary>
/// Partial refund issued because replacement costs less than original.
/// Issued upon exchange completion (after inspection passes).
/// </summary>
public sealed record ExchangePartialRefundIssued(
    Guid ReturnId,
    decimal RefundAmount,
    DateTimeOffset IssuedAt);
