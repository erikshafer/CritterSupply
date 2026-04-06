using Fulfillment.Shipments;
using Marten;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Wolverine.Http;

namespace Fulfillment.Api.OrderFulfillment;

/// <summary>
/// Response DTO for shipment queries.
/// </summary>
public sealed record ShipmentResponse(
    Guid Id,
    Guid OrderId,
    ShipmentStatus Status,
    string? Carrier,
    string? TrackingNumber,
    string? AssignedFulfillmentCenter,
    DateTimeOffset RequestedAt,
    DateTimeOffset? HandedToCarrierAt,
    DateTimeOffset? DeliveredAt,
    int DeliveryAttemptCount);

/// <summary>
/// HTTP GET endpoint to retrieve shipments for a specific order.
/// Used by CS agents for WISMO ("Where is my order?") tickets.
/// Phase 1: Returns all shipments for an order (typically 1, but supports split shipments in future).
/// </summary>
public sealed class GetShipmentsForOrder
{
    [WolverineGet("/api/fulfillment/shipments")]
    [Authorize(Policy = "CustomerService")]
    public static async Task<Ok<IReadOnlyList<ShipmentResponse>>> Handle(
        Guid orderId,
        IDocumentSession session,
        CancellationToken ct)
    {
        // Query all shipments for this order
        var shipments = await session.Query<Shipment>()
            .Where(s => s.OrderId == orderId)
            .OrderByDescending(s => s.RequestedAt)
            .Select(s => new ShipmentResponse(
                s.Id,
                s.OrderId,
                s.Status,
                s.Carrier,
                s.TrackingNumber,
                s.AssignedFulfillmentCenter,
                s.RequestedAt,
                s.HandedToCarrierAt,
                s.DeliveredAt,
                s.DeliveryAttemptCount))
            .ToListAsync(ct);

        return TypedResults.Ok<IReadOnlyList<ShipmentResponse>>(shipments);
    }
}
