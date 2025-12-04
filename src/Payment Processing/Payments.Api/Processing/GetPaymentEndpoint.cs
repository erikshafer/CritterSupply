using Marten;
using Payments.Processing;
using Wolverine.Http;

namespace Payments.Api.Processing;

/// <summary>
/// Wolverine HTTP endpoint for querying payments.
/// </summary>
public static class GetPaymentEndpoint
{
    /// <summary>
    /// Retrieves a payment by its identifier.
    /// </summary>
    /// <param name="paymentId">The payment identifier.</param>
    /// <param name="session">The Marten query session.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>200 with PaymentResponse if found, 404 if not found.</returns>
    [WolverineGet("/api/payments/{paymentId}")]
    public static async Task<IResult> Get(
        Guid paymentId,
        IQuerySession session,
        CancellationToken cancellationToken)
    {
        var payment = await session.Events
            .AggregateStreamAsync<Payment>(paymentId, token: cancellationToken);

        return payment is null
            ? Results.NotFound()
            : Results.Ok(PaymentResponse.From(payment));
    }
}
