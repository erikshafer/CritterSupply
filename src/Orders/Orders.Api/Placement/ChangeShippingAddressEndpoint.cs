using Marten;
using Microsoft.AspNetCore.Mvc;
using Orders.Placement;
using Wolverine;
using Wolverine.Http;

namespace Orders.Api.Placement;

public sealed record ChangeShippingAddressRequest(
    ShippingAddress NewShippingAddress,
    string Reason);

/// <summary>
/// HTTP endpoint for changing the shipping address on an existing order (M45.1 / S4).
///
/// Eligibility (per <see cref="OrderDecider.CanChangeShippingAddress"/>):
/// - 202 Accepted when the order is in <c>Placed</c>, <c>PendingPayment</c>,
///   <c>PaymentConfirmed</c>, <c>InventoryReserved</c>, or <c>OnHold</c>.
/// - 409 Conflict when the order is in <c>InventoryCommitted</c> or any later status —
///   the warehouse is already picking, and a recall / re-pick coordination is required.
///   Deferred to the Orders remaster per
///   <c>docs/planning/milestones/m45-1-order-post-placement-modifications-gap-memo.md</c>.
///
/// Note: This endpoint mutates the saga's <c>ShippingAddress</c> in place rather than
/// re-running the decider on a re-derived saga state. The same shape is used by
/// <see cref="CancelOrderEndpoint"/>.
/// </summary>
public static class ChangeShippingAddressEndpoint
{
    [WolverinePost("/api/orders/{orderId}/shipping-address")]
    public static async Task<(IResult, OutgoingMessages)> Handle(
        Guid orderId,
        [FromBody] ChangeShippingAddressRequest request,
        IQuerySession querySession)
    {
        var outgoing = new OutgoingMessages();

        if (request is null || request.NewShippingAddress is null)
            return (Results.BadRequest(new ProblemDetails
            {
                Detail = "New shipping address is required",
                Status = 400
            }), outgoing);

        if (string.IsNullOrWhiteSpace(request.Reason))
            return (Results.BadRequest(new ProblemDetails
            {
                Detail = "A reason for the address change is required",
                Status = 400
            }), outgoing);

        var order = await querySession.LoadAsync<Order>(orderId);

        if (order is null)
            return (Results.NotFound(new ProblemDetails
            {
                Detail = "Order not found",
                Status = 404
            }), outgoing);

        if (!OrderDecider.CanChangeShippingAddress(order.Status))
        {
            var detail = order.Status is OrderStatus.Cancelled
                ? "Cannot change shipping address: order is cancelled"
                : "Cannot change shipping address after the warehouse has begun picking. Please cancel and re-place the order.";
            return (Results.Conflict(new ProblemDetails { Detail = detail, Status = 409 }), outgoing);
        }

        outgoing.Add(new ChangeShippingAddress(orderId, request.NewShippingAddress, request.Reason));

        return (Results.Accepted(), outgoing);
    }
}
