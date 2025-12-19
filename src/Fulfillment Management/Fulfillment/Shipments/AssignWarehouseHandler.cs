using Marten;
using Microsoft.AspNetCore.Mvc;
using Wolverine.Http;
using Wolverine.Marten;

namespace Fulfillment.Shipments;

/// <summary>
/// Handler for AssignWarehouse command.
/// Assigns a warehouse to fulfill the shipment.
/// </summary>
public static class AssignWarehouseHandler
{
    public static ProblemDetails Before(
        AssignWarehouse command,
        Shipment? shipment)
    {
        if (shipment is null)
            return new ProblemDetails { Detail = "Shipment not found", Status = 404 };

        if (shipment.Status != ShipmentStatus.Pending)
            return new ProblemDetails
            {
                Detail = $"Cannot assign warehouse to shipment in {shipment.Status} status",
                Status = 400
            };

        return WolverineContinue.NoProblems;
    }

    [WolverinePost("/api/fulfillment/shipments/{shipmentId}/assign")]
    public static WarehouseAssigned Handle(
        AssignWarehouse command,
        [WriteAggregate] Shipment shipment)
    {
        return new WarehouseAssigned(
            command.WarehouseId,
            DateTimeOffset.UtcNow);
    }
}
