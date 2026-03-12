namespace Messages.Contracts.Returns;

/// <summary>
/// Integration message published by Returns BC when a return fails inspection.
/// Customer Experience BC shows rejection details; Orders BC clears return-in-progress flag.
/// </summary>
public sealed record ReturnRejected(
    Guid ReturnId,
    Guid OrderId,
    Guid CustomerId,
    string Reason,
    IReadOnlyList<ReturnedItem> Items,
    DateTimeOffset RejectedAt);
