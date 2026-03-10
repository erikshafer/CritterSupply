namespace VendorPortal.ChangeRequests;

/// <summary>
/// Lightweight read model for listing change requests.
/// Used by the GET /api/vendor-portal/change-requests endpoint.
/// </summary>
public sealed record ChangeRequestSummary(
    Guid Id,
    string Sku,
    ChangeRequestType Type,
    ChangeRequestStatus Status,
    string Title,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? ResolvedAt);
