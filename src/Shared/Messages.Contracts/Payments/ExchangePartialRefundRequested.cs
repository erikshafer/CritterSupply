namespace Messages.Contracts.Payments;

/// <summary>
/// Request published by the Returns BC when a cross-product exchange completes
/// with a cheaper replacement and the customer is owed a partial refund equal
/// to the price difference. Consumed by the Payments BC's
/// <c>IssueExchangePartialRefundHandler</c>, which looks up the original
/// <c>Payment</c> for <see cref="OrderId"/> and refunds the delta.
///
/// <para>
/// Idempotency: the receiving handler treats <see cref="ReturnId"/> as the
/// dedupe key. Re-deliveries under at-least-once semantics re-emit the
/// <see cref="ExchangePartialRefundIssued"/> reply once the refund has already
/// been applied — they do not double-refund.
/// </para>
/// </summary>
public sealed record ExchangePartialRefundRequested(
    Guid ReturnId,
    Guid OrderId,
    Guid CustomerId,
    decimal RefundAmount,
    DateTimeOffset RequestedAt);
