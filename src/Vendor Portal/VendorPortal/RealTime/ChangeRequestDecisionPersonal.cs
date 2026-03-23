namespace VendorPortal.RealTime;

/// <summary>
/// Pushed to <c>user:{userId}</c> as a personal decision notification.
/// Only the submitting vendor user receives this — shown as a toast with context.
/// Decision values: "Approved", "Rejected", "NeedsMoreInfo".
/// ChangeType values: "Description", "Image", "DataCorrection".
/// </summary>
public sealed record ChangeRequestDecisionPersonal(
    Guid VendorUserId,
    Guid RequestId,
    string Sku,
    string Decision,
    string? Reason,
    DateTimeOffset DecidedAt,
    string ChangeType) : IVendorUserMessage;
