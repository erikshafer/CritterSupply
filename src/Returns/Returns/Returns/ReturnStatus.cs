namespace Returns.Returns;

public enum ReturnStatus
{
    Requested,
    Approved,
    Denied,
    LabelGenerated, // Phase 2 — carrier integration
    InTransit,      // Phase 2 — carrier integration
    Received,
    Inspecting,
    Completed,
    Rejected,
    Expired
}
