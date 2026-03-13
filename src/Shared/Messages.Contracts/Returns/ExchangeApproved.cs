namespace Messages.Contracts.Returns;

/// <summary>
/// Integration message published by Returns BC when an exchange is approved after stock availability check.
/// Customer Experience BC updates UI; Notifications BC sends approval email with ship-by deadline.
/// </summary>
public sealed record ExchangeApproved(
    Guid ReturnId,
    Guid OrderId,
    Guid CustomerId,
    string ReplacementSku,
    decimal PriceDifference,
    DateTimeOffset ShipByDeadline,
    DateTimeOffset ApprovedAt);
