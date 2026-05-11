namespace Payments.Processing;

/// <summary>
/// Domain event when a refund is processed against a payment.
/// Persisted to the Marten event store.
///
/// <para>
/// <see cref="ReturnId"/> is optional and only set when the refund is
/// driven by the cross-product exchange partial-refund path
/// (M47.0 / Slice 2). It is the idempotency key used by
/// <c>IssueExchangePartialRefundHandler</c> to avoid double-refunding on
/// at-least-once redelivery of
/// <c>Messages.Contracts.Payments.ExchangePartialRefundRequested</c>.
/// Refunds driven by Orders / customer-service / cancellation flows leave
/// it null. Pre-M47 events deserialize with a null value (record default),
/// so the field is safe to add to the existing event stream without
/// migration.
/// </para>
/// </summary>
public sealed record PaymentRefunded(
    Guid PaymentId,
    decimal RefundAmount,
    decimal TotalRefunded,
    string RefundTransactionId,
    DateTimeOffset RefundedAt,
    Guid? ReturnId = null);
