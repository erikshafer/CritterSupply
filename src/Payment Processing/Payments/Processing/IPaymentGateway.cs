namespace Payments.Processing;

/// <summary>
/// Abstraction for payment gateway operations.
/// Implementations handle provider-specific details.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>
    /// Authorizes a payment (holds funds without capturing).
    /// Authorization typically expires after a provider-specific duration (e.g., 7 days).
    /// </summary>
    /// <param name="amount">The amount to authorize</param>
    /// <param name="currency">The currency code (e.g., "USD")</param>
    /// <param name="paymentMethodToken">The secure token representing the payment method</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result of the authorization operation</returns>
    Task<GatewayResult> AuthorizeAsync(
        decimal amount,
        string currency,
        string paymentMethodToken,
        CancellationToken cancellationToken);

    /// <summary>
    /// Captures funds for a payment (immediate capture, no prior authorization).
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
    /// Captures a previously authorized payment.
    /// </summary>
    /// <param name="authorizationId">The authorization identifier from AuthorizeAsync</param>
    /// <param name="amount">The amount to capture (must not exceed authorized amount)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result of the capture operation</returns>
    Task<GatewayResult> CaptureAuthorizedAsync(
        string authorizationId,
        decimal amount,
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
