namespace Returns.Returns;

public sealed record ReturnLineItem(
    string Sku,
    string ProductName,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal,
    ReturnReason Reason,
    string? Explanation = null);

public sealed record ReturnRequested(
    Guid ReturnId,
    Guid OrderId,
    Guid CustomerId,
    IReadOnlyList<ReturnLineItem> Items,
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

public sealed record ReturnExpired(
    Guid ReturnId,
    DateTimeOffset ExpiredAt);
