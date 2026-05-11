namespace Messages.Contracts.Payments;

/// <summary>
/// Reply published by the Payments BC when the price-difference delta for a
/// cross-product exchange has been successfully captured against the customer's
/// payment method. Consumed by the Returns BC's
/// <c>ExchangeDeltaCapturedHandler</c>, which appends the
/// <c>ExchangeAdditionalPaymentCaptured</c> domain event and republishes the
/// public <c>Messages.Contracts.Returns.ExchangeAdditionalPaymentCaptured</c>
/// to the Storefront / Orders / Backoffice fan-out.
///
/// <para>
/// See M47.0 / Slice 2 plan and ADR 0062 for choreography rationale and
/// the deterministic <see cref="PaymentId"/> derivation from <see cref="ReturnId"/>.
/// </para>
/// </summary>
public sealed record ExchangeDeltaCaptured(
    Guid ReturnId,
    Guid OrderId,
    Guid PaymentId,
    decimal AmountCaptured,
    string Currency,
    string TransactionId,
    DateTimeOffset CapturedAt);
