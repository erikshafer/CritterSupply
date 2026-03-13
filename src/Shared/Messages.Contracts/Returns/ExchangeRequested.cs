namespace Messages.Contracts.Returns;

/// <summary>
/// Integration message published by Returns BC when a customer initiates an exchange request.
/// Orders saga listens to mark exchange in-progress.
/// </summary>
public sealed record ExchangeRequested(
    Guid ReturnId,
    Guid OrderId,
    Guid CustomerId,
    string ReplacementSku,
    int ReplacementQuantity,
    decimal ReplacementUnitPrice,
    DateTimeOffset RequestedAt);
