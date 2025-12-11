namespace Payments.Processing;

/// <summary>
/// Integration event published when payment is successfully authorized.
/// Orders saga can decide whether to capture immediately or hold authorization.
/// </summary>
public sealed record PaymentAuthorizedIntegration(
    Guid PaymentId,
    Guid OrderId,
    decimal Amount,
    string AuthorizationId,
    DateTimeOffset AuthorizedAt,
    DateTimeOffset ExpiresAt);
