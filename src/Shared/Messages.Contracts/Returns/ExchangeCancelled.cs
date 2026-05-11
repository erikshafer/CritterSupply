namespace Messages.Contracts.Returns;

/// <summary>
/// M47.0 / Slice 4 — published by the Returns BC when a cross-product
/// exchange is cancelled because the additional-payment delta capture
/// failed downstream in the Payments BC. Closes the
/// "Additional payment capture fails — exchange cancelled" Gherkin
/// scenario in <c>docs/features/returns/cross-product-exchange.feature</c>.
///
/// <para>
/// Consumers:
/// <list type="bullet">
///   <item>Storefront BC — emits a customer-visible SignalR notification
///     with status <c>"Cancelled"</c>.</item>
///   <item>Orders BC — saga acknowledger so the Order can re-evaluate
///     "is this Order closeable?" without an active Return.</item>
///   <item>Backoffice BC — operations dashboard surface (future slice).</item>
/// </list>
/// </para>
///
/// <para>
/// <see cref="Reason"/> is a short machine-pivotable code (e.g.
/// <c>"PaymentCaptureFailed"</c>); <see cref="Message"/> is the
/// PO-approved customer-facing copy verbatim from the Gherkin
/// (<c>"Payment for price difference could not be processed.
/// Exchange cancelled."</c>).
/// </para>
/// </summary>
public sealed record ExchangeCancelled(
    Guid ReturnId,
    Guid OrderId,
    Guid CustomerId,
    string Reason,
    string Message,
    DateTimeOffset CancelledAt);
