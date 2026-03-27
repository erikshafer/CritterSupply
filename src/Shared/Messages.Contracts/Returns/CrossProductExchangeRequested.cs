namespace Messages.Contracts.Returns;

/// <summary>
/// Integration message published by Returns BC when a cross-product exchange is requested.
/// Orders BC and Customer Experience BC listen for cross-product exchange tracking.
/// </summary>
public sealed record CrossProductExchangeRequested(
    Guid ReturnId,
    Guid OrderId,
    Guid CustomerId,
    string OriginalSku,
    string ReplacementSku,
    decimal OriginalUnitPrice,
    decimal ReplacementUnitPrice,
    int Quantity,
    DateTimeOffset RequestedAt);
