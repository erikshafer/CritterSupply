namespace Messages.Contracts.Returns;

/// <summary>
/// Integration message published by Returns BC when a return request is denied.
/// Orders saga listens to this to clear the in-progress flag; if the return window
/// has already expired, the saga closes. Customer Experience BC shows denial details.
/// </summary>
public sealed record ReturnDenied(
    Guid ReturnId,
    Guid OrderId,
    Guid CustomerId,
    string Reason,
    string? Message,
    DateTimeOffset DeniedAt);
