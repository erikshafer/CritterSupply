using Marten;
using Orders.Placement;
using Wolverine.Http;

namespace Orders.Api.Placement;

/// <summary>
/// Wolverine HTTP endpoint for querying orders.
/// </summary>
public static class GetOrderEndpoint
{
    /// <summary>
    /// Retrieves an order by its identifier.
    /// </summary>
    /// <param name="orderId">The order identifier.</param>
    /// <param name="session">The Marten query session.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>200 with OrderResponse if found, 404 if not found.</returns>
    [WolverineGet("/api/orders/{orderId}")]
    public static async Task<IResult> Get(
        Guid orderId,
        IQuerySession session,
        CancellationToken cancellationToken)
    {
        var order = await session.LoadAsync<Order>(orderId, cancellationToken);

        return order is null
            ? Results.NotFound()
            : Results.Ok(OrderResponse.From(order));
    }
}
