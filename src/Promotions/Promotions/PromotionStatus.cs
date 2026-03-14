namespace Promotions;

/// <summary>
/// Lifecycle states for a promotion.
/// Phase 1: Draft → Active → (Paused ↔ Active) → Expired/Cancelled
/// </summary>
public enum PromotionStatus
{
    Draft = 0,      // Created but not yet active
    Active = 1,     // Currently active and applying to orders
    Paused = 2,     // Temporarily suspended
    Expired = 3,    // Ended naturally (EndDate reached)
    Cancelled = 4   // Manually terminated before EndDate
}
