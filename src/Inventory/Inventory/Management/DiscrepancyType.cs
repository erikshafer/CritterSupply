namespace Inventory.Management;

/// <summary>
/// Classifies the source of a stock discrepancy detected by the system.
/// </summary>
public enum DiscrepancyType
{
    /// <summary>Picker found fewer items than committed allocation.</summary>
    ShortPick,

    /// <summary>Picker found zero items at the bin (complete miss).</summary>
    ZeroPick,

    /// <summary>Cycle count revealed a mismatch between system and physical counts.</summary>
    CycleCount
}
