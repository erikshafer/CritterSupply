using Marten;
using Orders.Placement;
using Wolverine.Http;

namespace Orders.Api.Placement;

/// <summary>
/// Wolverine HTTP endpoint for listing orders.
/// </summary>
public static class ListOrdersEndpoint
{
    /// <summary>
    /// Retrieves orders for a specific customer.
    /// </summary>
    /// <param name="customerId">The customer identifier to filter by.</param>
    /// <param name="session">The Marten query session.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>200 with list of OrderSummaryResponse (may be empty).</returns>
    [WolverineGet("/api/orders")]
    public static async Task<IResult> List(
        Guid? customerId,
        IQuerySession session,
        CancellationToken cancellationToken)
    {
        if (customerId is null)
            return Results.BadRequest(new { error = "customerId query parameter is required" });

        var orders = await session
            .Query<Order>()
            .Where(o => o.CustomerId == customerId.Value)
            .OrderByDescending(o => o.PlacedAt)
            .ToListAsync(cancellationToken);

        var summaries = orders
            .Select(OrderSummaryResponse.From)
            .ToList();

        return Results.Ok(summaries);
    }
}

/// <summary>
/// Summary response DTO for order listings.
/// </summary>
public sealed record OrderSummaryResponse(
    Guid OrderId,
    Guid CustomerId,
    DateTimeOffset PlacedAt,
    string Status,
    decimal TotalAmount,
    int ItemCount)
{
    public static OrderSummaryResponse From(Order order) =>
        new(
            order.Id,
            order.CustomerId,
            order.PlacedAt,
            order.Status.ToString(),
            order.TotalAmount,
            order.LineItems.Count);
}
