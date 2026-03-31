using Backoffice.Clients;
using Backoffice.Composition;
using Microsoft.AspNetCore.Authorization;
using Wolverine.Http;

namespace Backoffice.Api.Queries;

/// <summary>
/// Query to get order detail view (CS workflow: order detail lookup)
/// Composes data from Orders BC + Customer Identity BC
/// </summary>
public static class GetOrderDetailViewQuery
{
    [WolverineGet("/api/backoffice/orders/{orderId}")]
    [Authorize(Policy = "CustomerService")]
    public static async Task<IResult> Handle(
        Guid orderId,
        IOrdersClient ordersClient,
        ICustomerIdentityClient customerClient,
        CancellationToken ct)
    {
        // Query Orders BC for order detail
        var order = await ordersClient.GetOrderAsync(orderId, ct);

        if (order is null)
            return Results.NotFound(new { message = "Order not found" });

        // Query Customer Identity BC for customer email
        var customer = await customerClient.GetCustomerAsync(order.CustomerId, ct);

        // Query Orders BC for returnable items
        var returnableItems = await ordersClient.GetReturnableItemsAsync(orderId, ct);

        // Map to composition view
        var lineItems = order.Items.Select(i => new OrderLineItemView(
            i.Sku,
            i.ProductName,
            i.Quantity,
            i.UnitPrice,
            i.Quantity * i.UnitPrice)).ToList();

        var returnableItemViews = returnableItems.Select(r => new ReturnableItemView(
            r.Sku,
            r.ProductName,
            r.Quantity,
            r.DeliveredAt,
            r.IsReturnable,
            r.IneligibilityReason)).ToList();

        var view = new OrderDetailView(
            order.Id,
            order.CustomerId,
            customer?.Email ?? "Unknown",
            order.PlacedAt,
            order.Status,
            order.TotalAmount,
            lineItems,
            order.CancellationReason,
            returnableItemViews.Any(r => r.IsReturnable),
            returnableItemViews);

        return Results.Ok(view);
    }
}
