using Marten;
using Microsoft.AspNetCore.Mvc;
using Wolverine.Http;
using Wolverine.Marten;

namespace Fulfillment.Shipments;

/// <summary>
/// Handler for ConfirmDelivery command.
/// Marks shipment as successfully delivered.
/// </summary>
public static class ConfirmDeliveryHandler
{
    public static ProblemDetails Before(
        ConfirmDelivery command,
        Shipment? shipment)
    {
        if (shipment is null)
            return new ProblemDetails { Detail = "Shipment not found", Status = 404 };

        if (shipment.Status != ShipmentStatus.Shipped)
            return new ProblemDetails
            {
                Detail = $"Cannot confirm delivery for shipment in {shipment.Status} status. Must be Shipped first.",
                Status = 400
            };

        return WolverineContinue.NoProblems;
    }

    [WolverinePost("/api/fulfillment/shipments/{shipmentId}/confirm-delivery")]
    public static ShipmentDelivered Handle(
        ConfirmDelivery command,
        [WriteAggregate] Shipment shipment)
    {
        return new ShipmentDelivered(
            DateTimeOffset.UtcNow,
            command.RecipientName);
    }
}
