using Storefront.Clients;
using Wolverine.Http;

namespace Storefront.Api.Queries;

/// <summary>
/// BFF endpoint to list orders for a customer.
/// Delegates to Orders BC via IOrdersClient.GetOrderSummariesAsync().
/// </summary>
public static class GetOrderHistoryHandler
{
    [WolverineGet("/api/storefront/orders")]
    public static async Task<IResult> Handle(
        Guid customerId,
        IOrdersClient ordersClient,
        CancellationToken ct)
    {
        try
        {
            var orders = await ordersClient.GetOrderSummariesAsync(customerId, ct);
            return Results.Ok(orders);
        }
        catch (HttpRequestException ex)
        {
            return Results.Problem(
                title: "Failed to load order history",
                detail: ex.Message,
                statusCode: (int)(ex.StatusCode ?? System.Net.HttpStatusCode.InternalServerError));
        }
    }
}
