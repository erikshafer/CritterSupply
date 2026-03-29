using Microsoft.AspNetCore.Authorization;
using Storefront.Clients;
using Wolverine.Http;

namespace Storefront.Api.Queries;

/// <summary>
/// Query to get an order by ID via the Orders BC.
/// Used by the OrderConfirmation page to verify the order exists and retrieve its current status.
/// </summary>
public static class GetOrderViewHandler
{
    [WolverineGet("/api/storefront/orders/{orderId}")]
    [Authorize]
    public static async Task<IResult> Handle(
        Guid orderId,
        IOrdersClient ordersClient,
        CancellationToken ct)
    {
        try
        {
            var order = await ordersClient.GetOrderAsync(orderId, ct);
            return order is null
                ? Results.NotFound()
                : Results.Ok(new OrderStatusView(order.Id, order.Status, order.PlacedAt));
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                return Results.NotFound();
            throw;
        }
    }
}

/// <summary>
/// Minimal order view returned to the OrderConfirmation page.
/// </summary>
public sealed record OrderStatusView(
    Guid OrderId,
    string Status,
    DateTimeOffset PlacedAt);
