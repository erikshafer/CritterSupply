using Marten;

namespace Payments.Processing;

/// <summary>
/// Wolverine handler for AuthorizePayment commands.
/// Processes payment authorization requests by calling the gateway and persisting events.
/// </summary>
public static class AuthorizePaymentHandler
{
    /// <summary>
    /// Handles an AuthorizePayment command using two-phase auth/capture flow.
    /// </summary>
    /// <param name="command">The authorization request command</param>
    /// <param name="gateway">The payment gateway</param>
    /// <param name="session">The Marten document session</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Integration event for Orders context (PaymentAuthorizedIntegration or PaymentFailedIntegration)</returns>
    public static async Task<object> Handle(
        AuthorizePayment command,
        IPaymentGateway gateway,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        // Requirement 1.1: Create payment aggregate
        var payment = Payment.Create(new PaymentRequested(
            command.OrderId,
            command.CustomerId,
            command.Amount,
            command.Currency,
            command.PaymentMethodToken));

        // Requirement 2.1: Call gateway AuthorizeAsync
        var result = await gateway.AuthorizeAsync(
            command.Amount,
            command.Currency,
            command.PaymentMethodToken,
            cancellationToken);

        var processedAt = DateTimeOffset.UtcNow;

        // Requirement 2.2 & 2.3: Handle success or failure
        if (result.Success)
        {
            // Authorization expires in 7 days (typical for most payment processors)
            var expiresAt = processedAt.AddDays(7);

            var (authorizedPayment, integrationMessage) = payment.Authorize(
                result.TransactionId!,
                processedAt,
                expiresAt);

            // Persist payment events to Marten event store
            session.Events.StartStream<Payment>(authorizedPayment.Id, authorizedPayment.PendingEvents.ToArray());

            return integrationMessage;
        }

        // Handle authorization failure
        var (failedPayment, failureMessage) = payment.Fail(
            result.FailureReason ?? "Unknown authorization failure",
            result.IsRetriable,
            processedAt);

        // Persist failure events to Marten event store
        session.Events.StartStream<Payment>(failedPayment.Id, failedPayment.PendingEvents.ToArray());

        return failureMessage;
    }
}
