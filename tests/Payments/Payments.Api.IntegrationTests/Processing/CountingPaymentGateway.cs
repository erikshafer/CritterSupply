using Payments.Processing;

namespace Payments.Api.IntegrationTests.Processing;

/// <summary>
/// Test-only <see cref="IPaymentGateway"/> wrapper that delegates to the
/// real <see cref="StubPaymentGateway"/> while counting calls. Used by
/// the M47.0 / Slice 2 capture-delta idempotency test to assert that a
/// duplicate <c>ExchangeAdditionalPaymentRequired</c> delivery does not
/// hit the gateway twice. Registered as a singleton in
/// <see cref="TestFixture"/>.
/// </summary>
public sealed class CountingPaymentGateway : IPaymentGateway
{
    private readonly StubPaymentGateway _inner = new();
    private int _captureCalls;
    private int _refundCalls;

    public int CaptureCalls => Volatile.Read(ref _captureCalls);
    public int RefundCalls => Volatile.Read(ref _refundCalls);

    public void Reset()
    {
        Interlocked.Exchange(ref _captureCalls, 0);
        Interlocked.Exchange(ref _refundCalls, 0);
    }

    public Task<GatewayResult> AuthorizeAsync(decimal amount, string currency, string paymentMethodToken, CancellationToken cancellationToken)
        => _inner.AuthorizeAsync(amount, currency, paymentMethodToken, cancellationToken);

    public Task<GatewayResult> CaptureAsync(decimal amount, string currency, string paymentMethodToken, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _captureCalls);
        return _inner.CaptureAsync(amount, currency, paymentMethodToken, cancellationToken);
    }

    public Task<GatewayResult> CaptureAuthorizedAsync(string authorizationId, decimal amount, CancellationToken cancellationToken)
        => _inner.CaptureAuthorizedAsync(authorizationId, amount, cancellationToken);

    public Task<GatewayResult> RefundAsync(string transactionId, decimal amount, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _refundCalls);
        return _inner.RefundAsync(transactionId, amount, cancellationToken);
    }
}
