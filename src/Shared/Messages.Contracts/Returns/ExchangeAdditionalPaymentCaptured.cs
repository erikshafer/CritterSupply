namespace Messages.Contracts.Returns;

/// <summary>
/// Integration message published by Returns BC when the customer pays the additional amount
/// for a more expensive replacement in a cross-product exchange.
/// </summary>
public sealed record ExchangeAdditionalPaymentCaptured(
    Guid ReturnId,
    Guid OrderId,
    Guid CustomerId,
    decimal AmountCaptured,
    DateTimeOffset CapturedAt);
