namespace Messages.Contracts.Orders;

/// <summary>
/// Integration event published by Orders BC when an order is put on hold for manual review
/// (M45.1 / S5). Consumers:
/// - Backoffice: surface the order in the review queue.
/// - Customer Experience: notify the customer that their order is being reviewed.
/// - Fulfillment: pause any pre-pick activity (today: not yet wired — a hold pre-fulfillment
///   means InventoryCommitted has not yet been emitted, so Fulfillment naturally has nothing
///   to pause; this becomes relevant in the post-handoff remaster scope).
///
/// Reason and ReviewerId are intentionally non-PII (no human-readable customer detail) so the
/// event is safe to forward to logs / analytics.
/// </summary>
public sealed record OrderPutOnHold(
    Guid OrderId,
    Guid CustomerId,
    string Reason,
    string ReviewerId,
    DateTimeOffset HeldAt);

/// <summary>
/// Integration event published by Orders BC when a held order is released back into normal
/// processing after review (M45.1 / S5).
/// </summary>
public sealed record OrderReleasedFromHold(
    Guid OrderId,
    Guid CustomerId,
    string ReviewerId,
    string? ReleaseNotes,
    DateTimeOffset ReleasedAt);

/// <summary>
/// Integration event published by Orders BC when an order is rejected for fraud after review
/// (M45.1 / S5). Reuses the standard cancellation compensation choreography (Inventory release,
/// Payments refund) — the dedicated event exists so Customer Experience can surface a different
/// customer-facing message and Backoffice can flag the customer account, without changing the
/// downstream choreography.
/// </summary>
public sealed record OrderRejectedForFraud(
    Guid OrderId,
    Guid CustomerId,
    string Reason,
    string ReviewerId,
    DateTimeOffset RejectedAt);
