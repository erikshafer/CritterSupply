namespace Messages.Contracts.Payments;

/// <summary>
/// Reply published by the Payments BC when the partial refund for a
/// cross-product cheaper-replacement exchange has actually moved through
/// the gateway against the original <see cref="OriginalPaymentId"/>.
/// Consumed by the Returns BC's <c>ExchangePartialRefundIssuedHandler</c>,
/// which appends the <c>ExchangePartialRefundIssued</c> domain event and
/// republishes the public
/// <c>Messages.Contracts.Returns.ExchangePartialRefundIssued</c>.
///
/// <para>
/// See M47.0 / Slice 2 plan and ADR 0062 for the choreography decision
/// (Returns ↔ Payments direct, no Orders saga involvement).
/// </para>
/// </summary>
public sealed record ExchangePartialRefundIssued(
    Guid ReturnId,
    Guid OrderId,
    Guid OriginalPaymentId,
    decimal RefundAmount,
    string Currency,
    string TransactionId,
    DateTimeOffset IssuedAt);
