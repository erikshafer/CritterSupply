namespace Messages.Contracts.Returns;

/// <summary>
/// Integration message published by Returns BC when a return request is denied.
/// Orders saga listens to this to clear the in-progress flag; if the return window
/// has already expired, the saga closes.
/// </summary>
public sealed record ReturnDenied(
    Guid ReturnId,
    Guid OrderId,
    string Reason,
    DateTimeOffset DeniedAt);
