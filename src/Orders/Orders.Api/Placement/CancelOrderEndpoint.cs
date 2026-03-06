using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Http;
using Orders.Placement;

namespace Orders.Api.Placement;

public sealed record CancelOrderRequest(string Reason);

public static class CancelOrderEndpoint
{
    [WolverinePost("/api/orders/{orderId}/cancel")]
    public static async Task<IResult> Handle(
        Guid orderId,
        [FromBody] CancelOrderRequest request,
        IMessageBus bus)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            return Results.BadRequest(new ProblemDetails
            {
                Detail = "Cancellation reason is required",
                Status = 400
            });

        await bus.PublishAsync(new CancelOrder(orderId, request.Reason));

        return Results.Accepted();
    }
}
