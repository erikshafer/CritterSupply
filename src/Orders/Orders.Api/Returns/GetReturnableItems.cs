using Marten;
using Microsoft.AspNetCore.Mvc;
using Orders.Placement;
using Wolverine.Http;

namespace Orders.Api.Returns;

public sealed record ReturnableItemsResponse(
    Guid OrderId,
    IReadOnlyList<ReturnableItem> Items,
    DateTimeOffset? DeliveredAt);

public sealed record ReturnableItem(
    string Sku,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal);

public static class GetReturnableItemsEndpoint
{
    [WolverineGet("/api/orders/{orderId}/returnable-items")]
    public static async Task<IResult> Handle(
        Guid orderId,
        IDocumentSession session,
        CancellationToken ct)
    {
        var order = await session.LoadAsync<Order>(orderId, ct);

        if (order is null)
            return Results.NotFound(new ProblemDetails { Detail = "Order not found", Status = 404 });

        if (order.Status is not (OrderStatus.Delivered or OrderStatus.Closed))
            return Results.BadRequest(new ProblemDetails
            {
                Detail = "Order must be in Delivered status for returnable items query",
                Status = 400
            });

        var items = order.LineItems
            .Select(li => new ReturnableItem(li.Sku, li.Quantity, li.UnitPrice, li.LineTotal))
            .ToList();

        // DeliveredAt is persisted on the Order saga from Fulfillment.ShipmentDelivered (Phase 2).
        return Results.Ok(new ReturnableItemsResponse(orderId, items, order.DeliveredAt));
    }
}
