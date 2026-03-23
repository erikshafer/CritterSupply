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
    Expired
}
