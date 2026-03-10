namespace VendorPortal.ChangeRequests;

/// <summary>
/// The lifecycle status of a vendor change request.
/// </summary>
public enum ChangeRequestStatus
{
    /// <summary>Created by vendor; not yet submitted to Catalog BC.</summary>
    Draft,

    /// <summary>Submitted to Catalog BC; awaiting review.</summary>
    Submitted,

    /// <summary>Catalog BC approved the change. Terminal state.</summary>
    Approved,

    /// <summary>Catalog BC rejected the change. Terminal state.</summary>
    Rejected,

    /// <summary>Vendor withdrew the request. Terminal state.</summary>
    Withdrawn,

    /// <summary>Catalog BC needs additional information before processing.</summary>
    NeedsMoreInfo,

    /// <summary>
    /// Auto-withdrawn because a newer request for the same SKU+Type was submitted.
    /// Terminal state. <see cref="ChangeRequest.ReplacedByRequestId"/> holds the successor ID.
    /// </summary>
    Superseded
}
