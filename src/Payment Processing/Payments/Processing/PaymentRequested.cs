using Marten;

namespace Payments.Processing;

/// <summary>
/// Command from Orders context requesting payment capture.
/// </summary>
public sealed record PaymentRequested(
    Guid OrderId,
    Guid CustomerId,
    decimal Amount,
    string Currency,
    string PaymentMethodToken);

/// <summary>
/// Wolverine handler for PaymentRequested commands.
/// Creates a Payment, calls the gateway, and publishes the result.
/// </summary>
public static class PaymentRequestedHandler
{
    /// <summary>
    /// Handles the PaymentRequested command by creating a Payment,
    /// calling the payment gateway, and returning the integration message.
    /// </summary>
    public static async Task<object> Handle(
        PaymentRequested command,
        IPaymentGateway gateway,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        // Create payment aggregate
        var payment = Payment.Create(command);

        // Call gateway to capture funds
        var result = await gateway.CaptureAsync(
            command.Amount,
            command.Currency,
            command.PaymentMethodToken,
            cancellationToken);

        // Apply result to payment and get integration message
        object integrationMessage;
        Payment updatedPayment;

        if (result.Success)
        {
            (updatedPayment, var captured) = payment.Capture(result.TransactionId!, DateTimeOffset.UtcNow);
            integrationMessage = captured;
        }
        else
        {
            (updatedPayment, var failed) = payment.Fail(result.FailureReason!, result.IsRetriable, DateTimeOffset.UtcNow);
            integrationMessage = failed;
        }

        // Persist events to Marten event store
        session.Events.StartStream<Payment>(updatedPayment.Id, updatedPayment.PendingEvents);

        // Return integration message for Orders context (cascaded by Wolverine)
        return integrationMessage;
    }
}
