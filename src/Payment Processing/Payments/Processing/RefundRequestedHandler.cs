using Marten;

namespace Payments.Processing;

/// <summary>
/// Wolverine handler for RefundRequested commands.
/// Processes refund requests by validating the original payment and calling the gateway.
/// </summary>
public static class RefundRequestedHandler
{
    /// <summary>
    /// Handles a RefundRequested command.
    /// </summary>
    /// <param name="command">The refund request command</param>
    /// <param name="gateway">The payment gateway</param>
    /// <param name="session">The Marten document session</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Integration event for Orders context (RefundCompleted or RefundFailed)</returns>
    public static async Task<object> Handle(
        RefundRequested command,
        IPaymentGateway gateway,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        // Requirement 5.1: Load original payment and validate it exists and was captured
        var payment = await session.Events
            .AggregateStreamAsync<Payment>(command.PaymentId, token: cancellationToken);

        if (payment is null)
        {
            return new RefundFailed(
                command.PaymentId,
                command.OrderId,
                "Payment not found",
                DateTimeOffset.UtcNow);
        }

        if (payment.Status != PaymentStatus.Captured)
        {
            return new RefundFailed(
                command.PaymentId,
                command.OrderId,
                $"Payment is not in captured status. Current status: {payment.Status}",
                DateTimeOffset.UtcNow);
        }

        // Requirement 5.3: Validate refund amount does not exceed captured amount
        if (command.Amount > payment.Amount)
        {
            return new RefundFailed(
                command.PaymentId,
                command.OrderId,
                $"Refund amount ({command.Amount}) exceeds captured amount ({payment.Amount})",
                DateTimeOffset.UtcNow);
        }

        // Requirement 5.2: Call gateway RefundAsync
        var result = await gateway.RefundAsync(
            payment.TransactionId!,
            command.Amount,
            cancellationToken);

        var refundedAt = DateTimeOffset.UtcNow;

        // Requirement 5.4 & 5.5: Publish RefundCompleted or RefundFailed
        if (result.Success)
        {
            return new RefundCompleted(
                command.PaymentId,
                command.OrderId,
                command.Amount,
                result.TransactionId!,
                refundedAt);
        }

        return new RefundFailed(
            command.PaymentId,
            command.OrderId,
            result.FailureReason ?? "Unknown refund failure",
            refundedAt);
    }
}
