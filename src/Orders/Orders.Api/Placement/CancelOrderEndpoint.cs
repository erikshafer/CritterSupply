using Marten;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orders.Placement;
using Wolverine;
using Wolverine.Http;

namespace Orders.Api.Placement;

public sealed record CancelOrderRequest(string Reason);

public static class CancelOrderEndpoint
{
    [WolverinePost("/api/orders/{orderId}/cancel")]
    [Authorize(Policy = "CustomerService")]
    public static async Task<(IResult, OutgoingMessages)> Handle(
        Guid orderId,
        [FromBody] CancelOrderRequest request,
        IQuerySession querySession)
    {
        var outgoing = new OutgoingMessages();

        if (string.IsNullOrWhiteSpace(request.Reason))
            return (Results.BadRequest(new ProblemDetails
            {
                Detail = "Cancellation reason is required",
                Status = 400
            }), outgoing);

        var order = await querySession.LoadAsync<Order>(orderId);

        if (order is null)
            return (Results.NotFound(new ProblemDetails
            {
                Detail = "Order not found",
                Status = 404
            }), outgoing);

        if (!Orders.Placement.OrderDecider.CanBeCancelled(order.Status))
        {
            var detail = order.Status is OrderStatus.Cancelled
                ? "Order is already cancelled"
                : "Order cannot be cancelled after it has been shipped";
            return (Results.Conflict(new ProblemDetails { Detail = detail, Status = 409 }), outgoing);
        }

        outgoing.Add(new CancelOrder(orderId, request.Reason));

        return (Results.Accepted(), outgoing);
    }
}
