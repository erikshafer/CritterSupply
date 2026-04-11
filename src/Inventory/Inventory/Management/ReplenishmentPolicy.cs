namespace Inventory.Management;

/// <summary>
/// Inline policy that determines when replenishment should be triggered.
/// Fires when available stock is below the low-stock threshold AND there are pending backorders.
/// Called from any handler that decrements stock (transfers, quarantine, damage, write-off).
/// </summary>
public static class ReplenishmentPolicy
{
    /// <summary>
    /// Returns true when the new available quantity is below the low-stock threshold
    /// and there are pending backorders — indicating urgent need for replenishment.
    /// </summary>
    public static bool ShouldTrigger(int newAvailableQuantity, bool hasPendingBackorders) =>
        newAvailableQuantity < LowStockPolicy.DefaultThreshold && hasPendingBackorders;
}
