using Fulfillment.Shipments;
using Microsoft.AspNetCore.Authorization;
using Wolverine;
using Wolverine.Http;

namespace Fulfillment.Api.OrderFulfillment;

/// <summary>
/// HTTP POST endpoint for carrier webhook events.
/// Receives carrier scan events (in-transit, delivered, delivery attempted, etc.)
/// and delegates to CarrierWebhookHandler via Wolverine message dispatch.
/// </summary>
public sealed class CarrierWebhookEndpoint
{
    [WolverinePost("/api/fulfillment/carrier-webhook")]
    [Authorize(Policy = "AnyAuthenticated")]
    public static async Task<IResult> Handle(
        CarrierWebhookPayload payload,
        IMessageBus bus,
        CancellationToken ct)
    {
        await bus.InvokeAsync(payload, ct);
        return Results.Accepted();
    }
}
