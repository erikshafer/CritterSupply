namespace Returns.ReturnProcessing;

/// <summary>
/// Discriminates between refund (cash back) and exchange (replacement item) returns.
/// Phase 1: Same-SKU only, replacement must cost same or less (no upcharge collection).
/// </summary>
public enum ReturnType
{
    Refund,
    Exchange
}
