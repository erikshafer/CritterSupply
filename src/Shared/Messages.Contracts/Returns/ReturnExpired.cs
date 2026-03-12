namespace Messages.Contracts.Returns;

/// <summary>
/// Integration message published by Returns BC when an approved return expires
/// (customer never shipped within the 30-day window).
/// Notifications BC sends expiration notice; Orders saga clears return flag.
/// </summary>
public sealed record ReturnExpired(
    Guid ReturnId,
    Guid OrderId,
    Guid CustomerId,
    DateTimeOffset ExpiredAt);
