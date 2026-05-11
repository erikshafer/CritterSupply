using Marten;
using Wolverine;
using ReturnsMessages = Messages.Contracts.Returns;
using PaymentsMessages = Messages.Contracts.Payments;

namespace Payments.Processing;

/// <summary>
/// Captures the price-difference delta for a cross-product exchange where the
/// replacement costs more than the original. Listens for
/// <c>Messages.Contracts.Returns.ExchangeAdditionalPaymentRequired</c>
/// (M47.0 / Slice 2) and replies with either
/// <see cref="PaymentsMessages.ExchangeDeltaCaptured"/> or
/// <see cref="PaymentsMessages.ExchangeDeltaCaptureFailed"/>.
///
/// <para>
/// Piggybacks on the existing <see cref="Payment"/> aggregate (no new
/// <c>ExchangePayment</c> aggregate). The delta capture starts a NEW
/// <see cref="Payment"/> stream whose id is derived deterministically from
/// the <c>ReturnId</c> via <see cref="ExchangePaymentIds.ComputeDeltaPaymentId"/>.
/// See ADR 0062 for the choreography + aggregate-piggyback decisions.
/// </para>
///
/// <para>
/// Idempotency: under at-least-once redelivery, a second invocation with
/// the same <c>ReturnId</c> finds an existing <see cref="Payment"/> stream
/// at the deterministic id. We re-emit the success / failure reply
/// (matching the Slice 1 inventory pattern of "re-emit on duplicate, never
/// silently drop") and do NOT call the gateway a second time, do NOT
/// append further events.
/// </para>
///
/// <para>
/// The customer's payment method token + currency are sourced from the
/// original <see cref="Payment"/> for the same <c>OrderId</c>. The first
/// captured payment is used; if no captured payment exists, the handler
/// emits <see cref="PaymentsMessages.ExchangeDeltaCaptureFailed"/> with
/// <c>IsRetriable=false</c> so Slice 4 can drive the deny path.
/// </para>
/// </summary>
public static class CaptureExchangeDeltaHandler
{
    /// <summary>
    /// Fallback currency used on the no-original-payment failure path
    /// (the only flow where the handler cannot derive a real currency
    /// from a captured original <see cref="Payment"/>). The Payments BC
    /// is single-currency in M47.0; once a multi-currency story exists
    /// (currently out of scope), this should move to configuration.
    /// </summary>
    private const string FallbackCurrency = "USD";

    /// <summary>
    /// Synthetic payment-method token used for the no-original-payment
    /// failure path so the failed <see cref="Payment"/> stream is
    /// well-formed (a non-empty token is required by
    /// <see cref="PaymentInitiated"/>). Never reaches a real gateway —
    /// the failure event is appended in the same call.
    /// </summary>
    private const string UnknownPaymentToken = "tok_unknown";

    /// <summary>
    /// Loads the (possibly existing) delta-capture Payment so the handler
    /// can detect duplicate deliveries before calling the gateway.
    /// </summary>
    public static async Task<Payment?> LoadAsync(
        ReturnsMessages.ExchangeAdditionalPaymentRequired message,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        var deltaPaymentId = ExchangePaymentIds.ComputeDeltaPaymentId(message.ReturnId);
        return await session.LoadAsync<Payment>(deltaPaymentId, cancellationToken);
    }

    public static async Task<OutgoingMessages> HandleAsync(
        ReturnsMessages.ExchangeAdditionalPaymentRequired message,
        Payment? existingDeltaPayment,
        IDocumentSession session,
        IPaymentGateway gateway,
        CancellationToken cancellationToken)
    {
        var outgoing = new OutgoingMessages();
        var deltaPaymentId = ExchangePaymentIds.ComputeDeltaPaymentId(message.ReturnId);

        // -------------------------------------------------------------------
        // Idempotency branch — duplicate delivery of the same ReturnId.
        // Mirror the Slice 1 inventory pattern: re-emit the success / failure
        // reply so a Returns redelivery loss does not strand the exchange.
        // Do NOT touch the gateway a second time and do NOT append events.
        // -------------------------------------------------------------------
        if (existingDeltaPayment is not null)
        {
            if (existingDeltaPayment.Status == PaymentStatus.Captured)
            {
                outgoing.Add(new PaymentsMessages.ExchangeDeltaCaptured(
                    ReturnId: message.ReturnId,
                    OrderId: message.OrderId,
                    PaymentId: existingDeltaPayment.Id,
                    AmountCaptured: existingDeltaPayment.Amount,
                    Currency: existingDeltaPayment.Currency,
                    TransactionId: existingDeltaPayment.TransactionId ?? string.Empty,
                    CapturedAt: existingDeltaPayment.ProcessedAt ?? DateTimeOffset.UtcNow));
                return outgoing;
            }

            if (existingDeltaPayment.Status == PaymentStatus.Failed)
            {
                outgoing.Add(new PaymentsMessages.ExchangeDeltaCaptureFailed(
                    ReturnId: message.ReturnId,
                    OrderId: message.OrderId,
                    AmountDue: existingDeltaPayment.Amount,
                    Currency: existingDeltaPayment.Currency,
                    Reason: existingDeltaPayment.FailureReason ?? "Previous capture attempt failed",
                    IsRetriable: existingDeltaPayment.IsRetriable,
                    FailedAt: existingDeltaPayment.ProcessedAt ?? DateTimeOffset.UtcNow));
                return outgoing;
            }

            // Pending state shouldn't be reachable under inline snapshots,
            // but if it ever is, treat the redelivery as a no-op rather
            // than racing with the in-flight original.
            return outgoing;
        }

        // -------------------------------------------------------------------
        // First-time path — look up the original captured Payment to source
        // the customer's PaymentMethodToken + Currency, then capture the
        // delta against the same token.
        // -------------------------------------------------------------------
        var original = await session.Query<Payment>()
            .Where(p => p.OrderId == message.OrderId && p.Status == PaymentStatus.Captured)
            .OrderBy(p => p.InitiatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;

        if (original is null)
        {
            // No original captured payment — surface as a non-retriable
            // failure so Slice 4's deny-on-capture-failure path can act.
            // Stream is started so re-deliveries hit the idempotency branch.
            var initiated = new PaymentInitiated(
                deltaPaymentId,
                message.OrderId,
                message.CustomerId,
                message.AmountDue,
                FallbackCurrency,
                UnknownPaymentToken,
                now);

            const string reason = "No captured original payment found for this order; cannot reuse a payment method for the exchange delta capture.";
            var failedEvent = new PaymentFailed(deltaPaymentId, reason, IsRetriable: false, now);
            session.Events.StartStream<Payment>(deltaPaymentId, initiated, failedEvent);

            outgoing.Add(new PaymentsMessages.ExchangeDeltaCaptureFailed(
                ReturnId: message.ReturnId,
                OrderId: message.OrderId,
                AmountDue: message.AmountDue,
                Currency: FallbackCurrency,
                Reason: reason,
                IsRetriable: false,
                FailedAt: now));
            return outgoing;
        }

        var initiatedEvent = new PaymentInitiated(
            deltaPaymentId,
            message.OrderId,
            message.CustomerId,
            message.AmountDue,
            original.Currency,
            original.PaymentMethodToken,
            now);

        var gatewayResult = await gateway.CaptureAsync(
            message.AmountDue,
            original.Currency,
            original.PaymentMethodToken,
            cancellationToken);

        if (!gatewayResult.Success)
        {
            var failedEvent = new PaymentFailed(
                deltaPaymentId,
                gatewayResult.FailureReason ?? "Unknown capture failure",
                gatewayResult.IsRetriable,
                now);
            session.Events.StartStream<Payment>(deltaPaymentId, initiatedEvent, failedEvent);

            outgoing.Add(new PaymentsMessages.ExchangeDeltaCaptureFailed(
                ReturnId: message.ReturnId,
                OrderId: message.OrderId,
                AmountDue: message.AmountDue,
                Currency: original.Currency,
                Reason: gatewayResult.FailureReason ?? "Unknown capture failure",
                IsRetriable: gatewayResult.IsRetriable,
                FailedAt: now));
            return outgoing;
        }

        var capturedEvent = new PaymentCaptured(deltaPaymentId, gatewayResult.TransactionId!, now);
        session.Events.StartStream<Payment>(deltaPaymentId, initiatedEvent, capturedEvent);

        outgoing.Add(new PaymentsMessages.ExchangeDeltaCaptured(
            ReturnId: message.ReturnId,
            OrderId: message.OrderId,
            PaymentId: deltaPaymentId,
            AmountCaptured: message.AmountDue,
            Currency: original.Currency,
            TransactionId: gatewayResult.TransactionId!,
            CapturedAt: now));

        return outgoing;
    }
}
