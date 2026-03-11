namespace Messages.Contracts.Returns;

/// <summary>
/// Integration message published by Returns BC when a customer initiates a return request.
/// Orders saga listens to this to mark the return as in-progress and prevent premature saga closure.
/// </summary>
public sealed record ReturnRequested(
    Guid ReturnId,
    Guid OrderId,
    Guid CustomerId,
    DateTimeOffset RequestedAt);
