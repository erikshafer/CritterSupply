using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;
using IntegrationMessages = Messages.Contracts.Fulfillment;

namespace Fulfillment.Shipments;

public sealed record ConfirmDelivery(
    Guid ShipmentId,
    string? RecipientName = null)
{
    public class ConfirmDeliveryValidator : AbstractValidator<ConfirmDelivery>
    {
        public ConfirmDeliveryValidator()
        {
            RuleFor(x => x.ShipmentId).NotEmpty();
            RuleFor(x => x.RecipientName).MaximumLength(200);
        }
    }
}

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
    [Authorize]
    public static (Events, OutgoingMessages) Handle(
        ConfirmDelivery command,
        [WriteAggregate] Shipment shipment)
    {
        var deliveredAt = DateTimeOffset.UtcNow;

        var domainEvent = new ShipmentDelivered(deliveredAt, command.RecipientName);

        var events = new Events();
        events.Add(domainEvent);

        var integrationMessage = new IntegrationMessages.ShipmentDelivered(
            shipment.OrderId,
            command.ShipmentId,
            deliveredAt,
            command.RecipientName);

        var outgoing = new OutgoingMessages();
        outgoing.Add(integrationMessage);

        return (events, outgoing);
    }
}
