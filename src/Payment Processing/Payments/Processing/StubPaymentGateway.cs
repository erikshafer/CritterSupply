namespace Payments.Processing;

/// <summary>
/// Stub gateway for testing. Behavior controlled by token patterns:
/// - "tok_success_*" -> Success
/// - "tok_decline_*" -> Decline failure (non-retriable)
/// - "tok_timeout_*" -> Retriable timeout
/// - Any other token -> Success (default behavior)
/// </summary>
public sealed class StubPaymentGateway : IPaymentGateway
{
    public Task<GatewayResult> AuthorizeAsync(
        decimal amount,
        string currency,
        string paymentMethodToken,
        CancellationToken cancellationToken)
    {
        var result = paymentMethodToken switch
        {
            _ when paymentMethodToken.StartsWith("tok_success") =>
                new GatewayResult(true, $"auth_{Guid.NewGuid():N}", null, false),
            _ when paymentMethodToken.StartsWith("tok_decline") =>
                new GatewayResult(false, null, "card_declined", false),
            _ when paymentMethodToken.StartsWith("tok_timeout") =>
                new GatewayResult(false, null, "gateway_timeout", true),
            _ => new GatewayResult(true, $"auth_{Guid.NewGuid():N}", null, false)
        };

        return Task.FromResult(result);
    }

    public Task<GatewayResult> CaptureAsync(
        decimal amount,
        string currency,
        string paymentMethodToken,
        CancellationToken cancellationToken)
    {
        var result = paymentMethodToken switch
        {
            _ when paymentMethodToken.StartsWith("tok_success") =>
                new GatewayResult(true, $"txn_{Guid.NewGuid():N}", null, false),
            _ when paymentMethodToken.StartsWith("tok_decline") =>
                new GatewayResult(false, null, "card_declined", false),
            _ when paymentMethodToken.StartsWith("tok_timeout") =>
                new GatewayResult(false, null, "gateway_timeout", true),
            _ => new GatewayResult(true, $"txn_{Guid.NewGuid():N}", null, false)
        };

        return Task.FromResult(result);
    }

    public Task<GatewayResult> CaptureAuthorizedAsync(
        string authorizationId,
        decimal amount,
        CancellationToken cancellationToken)
    {
        // In stub, always succeed for authorized captures
        return Task.FromResult(
            new GatewayResult(true, $"txn_{Guid.NewGuid():N}", null, false));
    }

    public Task<GatewayResult> RefundAsync(
        string transactionId,
        decimal amount,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(
            new GatewayResult(true, $"ref_{Guid.NewGuid():N}", null, false));
    }
}
