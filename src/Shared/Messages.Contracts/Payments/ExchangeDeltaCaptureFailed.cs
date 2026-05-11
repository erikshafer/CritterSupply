namespace Messages.Contracts.Payments;

/// <summary>
/// Reply published by the Payments BC when the price-difference delta for a
/// cross-product exchange could not be captured (gateway decline, no original
/// payment found, etc.). Consumed by the Returns BC.
///
/// <para>
/// The Slice 2 consumer is intentionally a logging / ops-signal stub. Slice 4
/// will turn this into the "exchange cancelled" customer-facing path per the
/// pending Gherkin scenario "Additional payment capture fails — exchange cancelled"
/// in <c>docs/features/returns/cross-product-exchange.feature</c>. The contract
/// is defined now so Slice 4 has a stable surface to bind to and so re-deliveries
/// of failures don't end up at "no handler" in the meantime.
/// </para>
/// </summary>
public sealed record ExchangeDeltaCaptureFailed(
    Guid ReturnId,
    Guid OrderId,
    decimal AmountDue,
    string Currency,
    string Reason,
    bool IsRetriable,
    DateTimeOffset FailedAt);
