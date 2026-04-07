using Wolverine.Http;
using Marten;
using Microsoft.AspNetCore.Mvc;

namespace Fulfillment.Shipments;

/// <summary>
/// Slice 39: 3PL handoff command.
/// Hands a shipment off to a third-party logistics provider.
/// After handoff, carrier lifecycle events continue to arrive via webhook as normal.
/// </summary>
public sealed record HandoffToThirdPartyLogistics(
    Guid ShipmentId,
    string PartnerName,
    string ExternalOrderId);

/// <summary>
/// Slice 39: 3PL handoff handler.
/// Precondition: Shipment must be in Assigned status and the FC must be TX-FC
/// (the stub 3PL partner).
/// </summary>
public static class HandoffToThirdPartyLogisticsHandler
{
    public static async Task<ProblemDetails> Before(
        HandoffToThirdPartyLogistics command,
        IDocumentSession session,
        CancellationToken ct)
    {
        var shipment = await session.LoadAsync<Shipment>(command.ShipmentId, ct);
        if (shipment is null)
            return new ProblemDetails { Detail = "Shipment not found", Status = 404 };

        if (shipment.Status != ShipmentStatus.Assigned)
            return new ProblemDetails
            {
                Detail = $"Cannot hand off to 3PL for shipment in {shipment.Status} status. Must be Assigned.",
                Status = 400
            };

        if (shipment.AssignedFulfillmentCenter != "TX-FC")
            return new ProblemDetails
            {
                Detail = $"Cannot hand off to 3PL — assigned FC is {shipment.AssignedFulfillmentCenter}, " +
                         "but only TX-FC supports 3PL handoff.",
                Status = 400
            };

        return WolverineContinue.NoProblems;
    }

    public static void Handle(
        HandoffToThirdPartyLogistics command,
        IDocumentSession session)
    {
        session.Events.Append(command.ShipmentId,
            new ThirdPartyLogisticsHandoff(
                command.PartnerName,
                command.ExternalOrderId,
                DateTimeOffset.UtcNow));
    }
}
