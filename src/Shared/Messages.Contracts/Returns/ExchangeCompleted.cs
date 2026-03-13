namespace Messages.Contracts.Returns;

/// <summary>
/// Integration message published by Returns BC when an exchange completes successfully.
/// Orders saga listens to mark exchange complete and issue price difference refund (if any).
/// </summary>
public sealed record ExchangeCompleted(
    Guid ReturnId,
    Guid OrderId,
    Guid CustomerId,
    decimal? PriceDifferenceRefund,
    DateTimeOffset CompletedAt);
