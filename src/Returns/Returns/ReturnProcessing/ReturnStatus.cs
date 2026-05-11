namespace Returns.ReturnProcessing;

public enum ReturnStatus
{
    Requested,
    Approved,
    Denied,
    LabelGenerated, // Phase 2 — carrier integration
    InTransit,      // Phase 2 — carrier integration
    Received,
    Inspecting,
    ExchangeShipping, // Exchange-specific: replacement item is being shipped
    Completed,
    Rejected,
    Expired,
    // M47.0 / Slice 4 — terminal status for a cross-product exchange that
    // was approved but could not be fulfilled because the additional-payment
    // delta capture failed downstream in Payments. Distinct from Denied
    // (up-front rejection at request time) and Rejected (failed inspection).
    Cancelled
}
