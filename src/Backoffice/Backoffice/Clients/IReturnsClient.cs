namespace Backoffice.Clients;

/// <summary>
/// HTTP client for querying Returns BC (admin use)
/// </summary>
public interface IReturnsClient
{
    /// <summary>
    /// List returns (CS workflow: return search with optional orderId filter)
    /// </summary>
    Task<IReadOnlyList<ReturnSummaryDto>> GetReturnsAsync(
        Guid? orderId = null,
        int? limit = null,
        CancellationToken ct = default);

    /// <summary>
    /// Get detailed return information (CS workflow: return detail view)
    /// </summary>
    Task<ReturnDetailDto?> GetReturnAsync(Guid returnId, CancellationToken ct = default);

    /// <summary>
    /// Approve a return request (CS workflow: return approval)
    /// </summary>
    Task ApproveReturnAsync(Guid returnId, CancellationToken ct = default);

    /// <summary>
    /// Deny a return request (CS workflow: return denial)
    /// </summary>
    Task DenyReturnAsync(Guid returnId, string reason, CancellationToken ct = default);
}

/// <summary>
/// Return summary DTO from Returns BC
/// </summary>
public sealed record ReturnSummaryDto(
    Guid Id,
    Guid OrderId,
    DateTime RequestedAt,
    string Status,
    string ReturnType);

/// <summary>
/// Return detail DTO from Returns BC
/// </summary>
public sealed record ReturnDetailDto(
    Guid Id,
    Guid OrderId,
    DateTime RequestedAt,
    string Status,
    string ReturnType,
    string Reason,
    IReadOnlyList<ReturnItemDto> Items,
    string? InspectionResult,
    string? DenialReason);

/// <summary>
/// Return item DTO
/// </summary>
public sealed record ReturnItemDto(
    string Sku,
    string ProductName,
    int Quantity,
    string Condition);
