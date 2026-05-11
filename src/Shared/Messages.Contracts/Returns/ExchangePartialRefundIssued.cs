namespace Messages.Contracts.Returns;

/// <summary>
/// Integration message published by Returns BC after the Payments BC has
/// actually issued the partial refund against the original payment for a
/// cheaper-replacement cross-product exchange. Reaches Storefront / Orders /
/// Backoffice as the customer-visible "the difference has been refunded"
/// signal.
///
/// <para>
/// Republished by the Returns BC's <c>ExchangePartialRefundIssuedHandler</c>
/// on receipt of <c>Messages.Contracts.Payments.ExchangePartialRefundIssued</c>
/// (M47.0 / Slice 2). <see cref="OriginalPaymentId"/> identifies the original
/// order's Payments stream the refund was applied to;
/// <see cref="TransactionId"/> is the gateway refund reference;
/// <see cref="Currency"/> is carried so downstream presenters can format
/// the amount without re-querying Payments.
/// </para>
/// </summary>
public sealed record ExchangePartialRefundIssued(
    Guid ReturnId,
    Guid OrderId,
    Guid CustomerId,
    Guid OriginalPaymentId,
    decimal RefundAmount,
    string Currency,
    string TransactionId,
    DateTimeOffset IssuedAt);
