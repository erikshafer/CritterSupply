namespace Payments.Processing;

/// <summary>
/// Domain event: Payment has been authorized (funds held but not captured).
/// </summary>
public sealed record PaymentAuthorized(
    Guid PaymentId,
    string AuthorizationId,
    DateTimeOffset AuthorizedAt);
