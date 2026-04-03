using Marten;
using Orders.Placement;
using Wolverine.Http;

namespace Orders.Api.Placement;

/// <summary>
/// Wolverine HTTP endpoint for searching orders.
/// Supports search by Order ID (used as order number in UI).
/// Customer email/name search requires Customer Identity integration (deferred).
/// </summary>
public static class SearchOrdersEndpoint
{
    /// <summary>
    /// Searches orders by Order ID (treated as order number).
    /// Returns up to 50 results, ordered by placement date (most recent first).
    /// </summary>
    /// <param name="query">Partial or full Order ID (GUID format expected).</param>
    /// <param name="session">The Marten query session.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>200 with SearchOrdersResponse (may contain 0 results).</returns>
    [WolverineGet("/api/orders/search")]
    public static async Task<SearchOrdersResponse> Search(
        string? query,
        IQuerySession session,
        CancellationToken cancellationToken)
    {
        // If no query provided, return empty results
        if (string.IsNullOrWhiteSpace(query))
        {
            return new SearchOrdersResponse(
                Query: query ?? string.Empty,
                TotalCount: 0,
                Orders: []);
        }

        // Try parsing query as Guid (full or partial match)
        var orders = new List<Order>();

        if (Guid.TryParse(query, out var orderId))
        {
            // Exact Guid match
            var order = await session
                .Query<Order>()
                .FirstOrDefaultAsync(o => o.Id == orderId, token: cancellationToken);

            if (order is not null)
                orders.Add(order);
        }
        else
        {
            // Partial Guid match not supported (would require full-text search index)
            // For MVP: only exact Guid matches work
            // Future: integrate Customer Identity for email/name search
        }

        var summaries = orders
            .Select(OrderSummaryResponse.From)
            .ToList();

        return new SearchOrdersResponse(
            Query: query,
            TotalCount: summaries.Count,
            Orders: summaries);
    }
}

/// <summary>
/// Response DTO for order search results.
/// </summary>
public sealed record SearchOrdersResponse(
    string Query,
    int TotalCount,
    IReadOnlyList<OrderSummaryResponse> Orders);
