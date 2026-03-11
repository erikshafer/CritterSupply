namespace Messages.Contracts.Returns;

/// <summary>
/// Integration message published by Returns BC when a return is fully processed.
/// Orders saga listens to this to trigger a refund and close the saga.
/// </summary>
public sealed record ReturnCompleted(
    Guid ReturnId,
    Guid OrderId,
    decimal FinalRefundAmount,
    DateTimeOffset CompletedAt);
