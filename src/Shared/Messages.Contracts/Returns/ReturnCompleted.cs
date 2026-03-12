namespace Messages.Contracts.Returns;

/// <summary>
/// Integration message published by Returns BC when a return is fully processed.
/// Orders saga listens to this to trigger a refund and close the saga.
/// Inventory BC listens to restock eligible items.
/// Carries per-item disposition data for autonomous downstream processing.
/// </summary>
public sealed record ReturnCompleted(
    Guid ReturnId,
    Guid OrderId,
    Guid CustomerId,
    decimal FinalRefundAmount,
    IReadOnlyList<ReturnedItem> Items,
    DateTimeOffset CompletedAt);
