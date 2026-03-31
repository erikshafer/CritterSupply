using Backoffice.Clients;
using Microsoft.AspNetCore.Authorization;
using Wolverine.Http;

namespace Backoffice.Api.Queries;

/// <summary>
/// Query to search orders by order ID (GUID format).
/// CS workflow: Order Search page — find orders by exact GUID match.
/// </summary>
public static class SearchOrdersQuery
{
    [WolverineGet("/api/backoffice/orders/search")]
    [Authorize(Policy = "CustomerService")]
    public static async Task<IResult> Handle(
        string query,
        IOrdersClient ordersClient,
        CancellationToken ct)
    {
        // Delegate to Orders BC via client interface
        var result = await ordersClient.SearchOrdersAsync(query, ct);

        return Results.Ok(result);
    }
}
