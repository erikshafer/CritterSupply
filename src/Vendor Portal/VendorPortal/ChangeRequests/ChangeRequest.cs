namespace VendorPortal.ChangeRequests;

/// <summary>
/// Marten document representing a vendor product change request.
/// Supports 7 states: Draft, Submitted, Approved, Rejected, Withdrawn, NeedsMoreInfo, Superseded.
///
/// Invariant: only one active (Draft/Submitted/NeedsMoreInfo) request per VendorTenantId + Sku + Type.
/// Submitting a new request for the same SKU+Type auto-supersedes any existing active request.
///
/// Multi-tenancy: all queries must filter by VendorTenantId (from JWT claims only — never request params).
/// </summary>
public sealed class ChangeRequest
{
    /// <summary>Unique request ID. Caller-supplied (deterministic for idempotency).</summary>
    public Guid Id { get; init; }

    /// <summary>Tenant that owns this request. Immutable after creation.</summary>
    public Guid VendorTenantId { get; init; }

    /// <summary>User who created this request.</summary>
    public Guid SubmittedByUserId { get; init; }

    /// <summary>The product SKU this change applies to.</summary>
    public string Sku { get; init; } = null!;

    /// <summary>What kind of change is being requested.</summary>
    public ChangeRequestType Type { get; init; }

    /// <summary>Current lifecycle status.</summary>
    public ChangeRequestStatus Status { get; set; }

    /// <summary>Short summary of the change requested (used in list views).</summary>
    public string Title { get; init; } = null!;

    /// <summary>Full details of the requested change (for Description and DataCorrection types).</summary>
    public string Details { get; init; } = null!;

    /// <summary>Optional additional notes from the vendor.</summary>
    public string? AdditionalNotes { get; set; }

    /// <summary>
    /// Storage keys for pre-uploaded images (Image type only).
    /// Claim-check pattern: images are uploaded first, then referenced here.
    /// </summary>
    public IReadOnlyList<string> ImageStorageKeys { get; init; } = [];

    /// <summary>Reason provided by Catalog BC when rejecting the request.</summary>
    public string? RejectionReason { get; set; }

    /// <summary>Question from Catalog BC when NeedsMoreInfo.</summary>
    public string? Question { get; set; }

    /// <summary>
    /// ID of the request that superseded this one (set when Status = Superseded).
    /// Enables UI to show "replaced by request #xxx" linkage.
    /// </summary>
    public Guid? ReplacedByRequestId { get; set; }

    /// <summary>When the request was created (Draft state).</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>When the request was submitted to Catalog BC.</summary>
    public DateTimeOffset? SubmittedAt { get; set; }

    /// <summary>When the request reached a terminal state (Approved/Rejected/Withdrawn/Superseded).</summary>
    public DateTimeOffset? ResolvedAt { get; set; }

    /// <summary>
    /// True when the request is in an active (non-terminal) state.
    /// Active states: Draft, Submitted, NeedsMoreInfo.
    /// </summary>
    public bool IsActive => Status is
        ChangeRequestStatus.Draft or
        ChangeRequestStatus.Submitted or
        ChangeRequestStatus.NeedsMoreInfo;
}
