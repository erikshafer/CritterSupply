using Marten;

namespace Payments.Processing;

/// <summary>
/// Wolverine handler for CapturePayment commands.
/// Processes capture requests for previously authorized payments.
/// </summary>
public static class CapturePaymentHandler
{
    /// <summary>
    /// Handles a CapturePayment command.
    /// </summary>
    /// <param name="command">The capture request command</param>
    /// <param name="gateway">The payment gateway</param>
    /// <param name="session">The Marten document session</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Integration event for Orders context (PaymentCapturedIntegration or PaymentFailedIntegration)</returns>
    public static async Task<object> Handle(
        CapturePayment command,
        IPaymentGateway gateway,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        // Requirement 4.1: Load authorized payment and validate it exists
        var payment = await session.Events
            .AggregateStreamAsync<Payment>(command.PaymentId, token: cancellationToken);

        if (payment is null)
        {
            return new PaymentFailedIntegration(
                command.PaymentId,
                command.OrderId,
                "Payment not found",
                false,
                DateTimeOffset.UtcNow);
        }

        // Requirement 4.2: Validate payment is in Authorized status
        if (payment.Status != PaymentStatus.Authorized)
        {
            return new PaymentFailedIntegration(
                command.PaymentId,
                command.OrderId,
                $"Payment is not in authorized status. Current status: {payment.Status}",
                false,
                DateTimeOffset.UtcNow);
        }

        // Requirement 4.3: Check if authorization has expired
        if (payment.AuthorizationExpiresAt.HasValue &&
            payment.AuthorizationExpiresAt.Value < DateTimeOffset.UtcNow)
        {
            var expiredPayment = payment with { Status = PaymentStatus.Failed };
            var (failedPayment, failureMessage) = expiredPayment.Fail(
                "Authorization has expired",
                false,
                DateTimeOffset.UtcNow);

            session.Events.Append(payment.Id, failedPayment.PendingEvents.ToArray());

            return failureMessage;
        }

        // Determine amount to capture (default to full amount if not specified)
        var amountToCapture = command.AmountToCapture ?? payment.Amount;

        // Requirement 4.4: Validate capture amount does not exceed authorized amount
        if (amountToCapture > payment.Amount)
        {
            return new PaymentFailedIntegration(
                command.PaymentId,
                command.OrderId,
                $"Capture amount ({amountToCapture}) exceeds authorized amount ({payment.Amount})",
                false,
                DateTimeOffset.UtcNow);
        }

        // Requirement 4.5: Call gateway CaptureAuthorizedAsync
        var result = await gateway.CaptureAuthorizedAsync(
            payment.AuthorizationId!,
            amountToCapture,
            cancellationToken);

        var capturedAt = DateTimeOffset.UtcNow;

        // Requirement 4.6 & 4.7: Handle success or failure
        if (result.Success)
        {
            var (capturedPayment, integrationMessage) = payment.CaptureAuthorized(
                result.TransactionId!,
                capturedAt);

            // Persist capture event to Marten event store
            session.Events.Append(payment.Id, capturedPayment.PendingEvents.ToArray());

            return integrationMessage;
        }

        // Handle capture failure
        var (updatedPayment, captureFailureMessage) = payment.Fail(
            result.FailureReason ?? "Unknown capture failure",
            result.IsRetriable,
            capturedAt);

        session.Events.Append(payment.Id, updatedPayment.PendingEvents.ToArray());

        return captureFailureMessage;
    }
}
