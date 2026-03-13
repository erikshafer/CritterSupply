namespace Messages.Contracts.Returns;

/// <summary>
/// Integration message published by Returns BC when an exchange is rejected due to inspection failure.
/// Orders saga listens to mark exchange complete (no refund issued).
/// </summary>
public sealed record ExchangeRejected(
    Guid ReturnId,
    Guid OrderId,
    Guid CustomerId,
    string FailureReason,
    DateTimeOffset RejectedAt);
