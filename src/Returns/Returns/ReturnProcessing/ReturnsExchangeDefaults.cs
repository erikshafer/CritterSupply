namespace Returns.ReturnProcessing;

/// <summary>
/// Defaults for the cross-product exchange flow. Centralized so the
/// (currently hardcoded) replacement warehouse can be lifted to a
/// configuration source — or replaced by a Fulfillment routing call —
/// without hunting through handlers. See ADR 0061's "Default warehouse
/// WH-01 for replacement holds" decision.
/// </summary>
public static class ReturnsExchangeDefaults
{
    /// <summary>
    /// Warehouse used for cross-product exchange replacement
    /// reservations until the Returns + Fulfillment remaster grows a
    /// routing-aware <c>RouteReplacementRequested</c> entry point.
    /// </summary>
    public const string ReplacementWarehouseId = "WH-01";
}
