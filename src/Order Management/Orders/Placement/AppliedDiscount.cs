namespace Orders.Placement;

/// <summary>
/// A discount that was applied during checkout.
/// </summary>
public sealed record AppliedDiscount(
    string Code,
    decimal Amount);
