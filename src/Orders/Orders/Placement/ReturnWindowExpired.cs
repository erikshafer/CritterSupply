namespace Orders.Placement;

/// <summary>
/// Internal scheduled message used to close an order after the return eligibility window expires.
/// Published when an order is delivered; consumed by the same Order saga after the configured window.
/// This allows the saga to remain open for return requests after delivery without staying open forever.
/// </summary>
public sealed record ReturnWindowExpired(Guid OrderId);
