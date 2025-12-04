namespace Payments.Processing;

/// <summary>
/// Abstraction for payment gateway operations.
/// Implementations handle provider-specific details.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>
    /// Captures funds for a payment.
    /// </summary>
    /// <param name="amount">The amount to capture</param>
    /// <param name="currency">The currency code (e.g., "USD")</param>
    /// <param name="paymentMethodToken">The secure token representing the payment method</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result of the capture operation</returns>
    Task<GatewayResult> CaptureAsync(
        decimal amount,
        string currency,
        string paymentMethodToken,
        CancellationToken cancellationToken);

    /// <summary>
    /// Refunds a previously captured payment.
    /// </summary>
    /// <param name="transactionId">The original transaction identifier</param>
    /// <param name="amount">The amount to refund</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result of the refund operation</returns>
    Task<GatewayResult> RefundAsync(
        string transactionId,
        decimal amount,
        CancellationToken cancellationToken);
}
