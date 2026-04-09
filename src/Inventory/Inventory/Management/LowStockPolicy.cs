namespace Inventory.Management;

/// <summary>
/// Inline policy for detecting low-stock threshold breaches.
/// Phase 1: hardcoded threshold of 10 units.
/// Phase 2+: configurable per-SKU thresholds.
/// </summary>
public static class LowStockPolicy
{
    public const int DefaultThreshold = 10;

    /// <summary>
    /// Returns true when available quantity crosses below the threshold from above/at it.
    /// Returns false when already below threshold (no duplicate alerts).
    /// </summary>
    public static bool CrossedThresholdDownward(int previousQty, int newQty) =>
        previousQty >= DefaultThreshold && newQty < DefaultThreshold;
}
