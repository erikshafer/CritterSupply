namespace Messages.Contracts.Returns;

/// <summary>
/// Integration message published by Returns BC after the Payments BC has
/// actually captured the price-difference delta for a more-expensive
/// replacement in a cross-product exchange. Reaches Storefront / Orders /
/// Backoffice as the customer-visible "the upcharge has been billed" signal.
///
/// <para>
/// Republished by the Returns BC's <c>ExchangeDeltaCapturedHandler</c> on
/// receipt of <c>Messages.Contracts.Payments.ExchangeDeltaCaptured</c>
/// (M47.0 / Slice 2). <see cref="PaymentReference"/> is the gateway
/// transaction id and <see cref="PaymentId"/> is the new Payments-side
/// stream id for the delta capture (deterministic from <see cref="ReturnId"/>
/// — see ADR 0062). <see cref="Currency"/> is carried so downstream
/// presenters can format the amount without re-querying Payments.
/// </para>
/// </summary>
public sealed record ExchangeAdditionalPaymentCaptured(
    Guid ReturnId,
    Guid OrderId,
    Guid CustomerId,
    Guid PaymentId,
    decimal AmountCaptured,
    string Currency,
    string PaymentReference,
    DateTimeOffset CapturedAt);
