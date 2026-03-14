namespace Promotions;

/// <summary>
/// Types of discounts a promotion can offer.
/// Phase 1: PercentageOff only. Phase 2: FixedAmountOff, FreeShipping.
/// </summary>
public enum DiscountType
{
    PercentageOff = 0,  // e.g., 15% off
    FixedAmountOff = 1, // e.g., $5 off (Phase 2)
    FreeShipping = 2    // e.g., free standard shipping (Phase 2)
}
