namespace Messages.Contracts.Payments;

/// <summary>
/// M47.0 / Slice 4 — request published by the Returns BC when an inspection
/// rejects an exchange that had already captured an additional-payment delta
/// (cross-product exchange + replacement was more expensive). Closes the
/// "Cross-product exchange with additional payment rejected — refund payment
/// difference" Gherkin scenario in
/// <c>docs/features/returns/cross-product-exchange.feature</c>.
///
/// <para>
/// Distinct from <see cref="ExchangePartialRefundRequested"/>, which targets
/// the original Order payment for the cheaper-replacement happy path. This
/// contract targets the Slice 2 delta-capture <c>Payment</c> stream
/// (deterministic id from
/// <c>Payments.Processing.ExchangePaymentIds.ComputeDeltaPaymentId(ReturnId)</c>)
/// and refunds the captured delta in full.
/// </para>
///
/// <para>
/// Idempotency: the receiving handler treats <see cref="ReturnId"/> as the
/// dedupe key. Re-deliveries re-emit the
/// <see cref="ExchangePartialRefundIssued"/> reply — they do not double-refund.
/// </para>
/// </summary>
public sealed record RefundExchangeDeltaRequested(
    Guid ReturnId,
    Guid OrderId,
    Guid CustomerId,
    decimal RefundAmount,
    DateTimeOffset RequestedAt);
