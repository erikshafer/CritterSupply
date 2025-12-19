using Marten;
using Microsoft.AspNetCore.Mvc;
using Wolverine.Http;
using Wolverine.Marten;

namespace Fulfillment.Shipments;

/// <summary>
/// Handler for DispatchShipment command.
/// Marks shipment as dispatched with carrier and tracking information.
/// </summary>
public static class DispatchShipmentHandler
{
    public static ProblemDetails Before(
        DispatchShipment command,
        Shipment? shipment)
    {
        if (shipment is null)
            return new ProblemDetails { Detail = "Shipment not found", Status = 404 };

        if (shipment.Status != ShipmentStatus.Assigned)
            return new ProblemDetails
            {
                Detail = $"Cannot dispatch shipment in {shipment.Status} status. Must be Assigned first.",
                Status = 400
            };

        return WolverineContinue.NoProblems;
    }

    [WolverinePost("/api/fulfillment/shipments/{shipmentId}/dispatch")]
    public static ShipmentDispatched Handle(
        DispatchShipment command,
        [WriteAggregate] Shipment shipment)
    {
        return new ShipmentDispatched(
            command.Carrier,
            command.TrackingNumber,
            DateTimeOffset.UtcNow);
    }
}
