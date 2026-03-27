namespace Messages.Contracts.Returns;

/// <summary>
/// Integration message published by Returns BC when a cross-product exchange replacement
/// costs more than the original. Customer must pay the difference before exchange can proceed.
/// </summary>
public sealed record ExchangeAdditionalPaymentRequired(
    Guid ReturnId,
    Guid OrderId,
    Guid CustomerId,
    decimal AmountDue,
    DateTimeOffset RequiredAt);
