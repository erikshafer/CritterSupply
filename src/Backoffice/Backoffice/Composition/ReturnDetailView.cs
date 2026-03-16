namespace Backoffice.Composition;

/// <summary>
/// Composition view for return detail (CS workflow: return detail view)
/// Aggregates data from Returns BC with order context
/// </summary>
public sealed record ReturnDetailView(
    Guid ReturnId,
    Guid OrderId,
    Guid CustomerId,
    DateTime RequestedAt,
    string Status,
    string ReturnType,
    string Reason,
    IReadOnlyList<ReturnItemView> Items,
    string? InspectionResult,
    string? DenialReason,
    bool CanApprove,
    bool CanDeny);

/// <summary>
/// Return item view model
/// </summary>
public sealed record ReturnItemView(
    string Sku,
    string ProductName,
    int Quantity,
    string Condition);
