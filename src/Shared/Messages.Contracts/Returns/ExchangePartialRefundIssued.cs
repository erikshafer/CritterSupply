namespace Messages.Contracts.Returns;

/// <summary>
/// Integration message published by Returns BC when a partial refund is issued for a
/// cross-product exchange where the replacement costs less than the original.
/// </summary>
public sealed record ExchangePartialRefundIssued(
    Guid ReturnId,
    Guid OrderId,
    Guid CustomerId,
    decimal RefundAmount,
    DateTimeOffset IssuedAt);
