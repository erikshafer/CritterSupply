using Marten;
using Microsoft.AspNetCore.Authorization;
using Payments.Processing;
using Wolverine.Http;

namespace Payments.Api.Queries;

/// <summary>
/// Wolverine HTTP endpoint for querying payments by order ID.
/// Used by Backoffice to display payment history for customer service workflows.
/// </summary>
public static class GetPaymentsForOrderEndpoint
{
    /// <summary>
    /// Retrieves all payments for a specific order.
    /// </summary>
    /// <param name="orderId">The order identifier.</param>
    /// <param name="session">The Marten query session.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>200 with list of PaymentResponse objects.</returns>
    [WolverineGet("/api/payments")]
    [Authorize(Policy = "CustomerService")]
    public static async Task<IResult> Get(
        Guid orderId,
        IQuerySession session,
        CancellationToken cancellationToken)
    {
        // Query all Payment aggregates for the given OrderId
        // Note: This is a full table scan approach for simplicity.
        // For production, consider adding a Marten projection indexed by OrderId.
        var payments = await session.Query<Payment>()
            .Where(p => p.OrderId == orderId)
            .ToListAsync(cancellationToken);

        var responses = payments.Select(PaymentResponse.From).ToList();

        return Results.Ok(responses);
    }
}
