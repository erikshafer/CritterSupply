using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Wolverine.Http;
using Wolverine.Marten;

namespace Fulfillment.Shipments;

public sealed record DispatchShipment(
    Guid ShipmentId,
    string Carrier,
    string TrackingNumber)
{
    public class DispatchShipmentValidator : AbstractValidator<DispatchShipment>
    {
        public DispatchShipmentValidator()
        {
            RuleFor(x => x.ShipmentId).NotEmpty();
            RuleFor(x => x.Carrier).NotEmpty().MaximumLength(100);
            RuleFor(x => x.TrackingNumber).NotEmpty().MaximumLength(100);
        }
    }
}

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
