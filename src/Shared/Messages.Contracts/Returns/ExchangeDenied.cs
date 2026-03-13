namespace Messages.Contracts.Returns;

/// <summary>
/// Integration message published by Returns BC when an exchange is denied.
/// Customer Experience BC updates UI; Notifications BC sends denial email with customer-facing message.
/// </summary>
public sealed record ExchangeDenied(
    Guid ReturnId,
    Guid OrderId,
    Guid CustomerId,
    string Reason,
    string Message,
    DateTimeOffset DeniedAt);
