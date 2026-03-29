using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;
using IntegrationMessages = Messages.Contracts.Fulfillment;

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
    [Authorize]
    public static (Events, OutgoingMessages) Handle(
        DispatchShipment command,
        [WriteAggregate] Shipment shipment)
    {
        var dispatchedAt = DateTimeOffset.UtcNow;

        var domainEvent = new ShipmentDispatched(
            command.Carrier,
            command.TrackingNumber,
            dispatchedAt);

        var events = new Events();
        events.Add(domainEvent);

        var integrationMessage = new IntegrationMessages.ShipmentDispatched(
            shipment.OrderId,
            command.ShipmentId,
            command.Carrier,
            command.TrackingNumber,
            dispatchedAt);

        var outgoing = new OutgoingMessages();
        outgoing.Add(integrationMessage);

        return (events, outgoing);
    }
}
