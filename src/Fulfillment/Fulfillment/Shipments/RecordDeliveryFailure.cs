using FluentValidation;
using Marten;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;
using IntegrationMessages = Messages.Contracts.Fulfillment;

namespace Fulfillment.Shipments;

public sealed record RecordDeliveryFailure(
    Guid ShipmentId,
    string Reason)
{
    public class RecordDeliveryFailureValidator : AbstractValidator<RecordDeliveryFailure>
    {
        public RecordDeliveryFailureValidator()
        {
            RuleFor(x => x.ShipmentId).NotEmpty();
            RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
        }
    }
}

public static class RecordDeliveryFailureHandler
{
    // NOTE: We load the Shipment snapshot manually here rather than using the standard
    // compound-handler pattern (nullable Shipment? with [WriteAggregate] on Handle())
    // because Wolverine's [WriteAggregate] short-circuits the pipeline with a 400 when
    // the event stream doesn't exist, preventing Before() from returning a 404.
    // LoadAsync<Shipment> reads the inline snapshot document, which is always current
    // (SnapshotLifecycle.Inline).
    public static async Task<ProblemDetails> Before(
        RecordDeliveryFailure command,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        var shipment = await session.LoadAsync<Shipment>(command.ShipmentId, cancellationToken);

        if (shipment is null)
            return new ProblemDetails { Detail = "Shipment not found", Status = 404 };

        if (shipment.Status != ShipmentStatus.Shipped)
            return new ProblemDetails
            {
                Detail = $"Cannot record delivery failure for shipment in {shipment.Status} status. Must be Shipped first.",
                Status = 400
            };

        return WolverineContinue.NoProblems;
    }

    [WolverinePost("/api/fulfillment/shipments/{shipmentId}/record-delivery-failure")]
    [Authorize]
    public static (Events, OutgoingMessages) Handle(
        RecordDeliveryFailure command,
        [WriteAggregate] Shipment shipment)
    {
        var failedAt = DateTimeOffset.UtcNow;

        var domainEvent = new ShipmentDeliveryFailed(command.Reason, failedAt);
        var events = new Events();
        events.Add(domainEvent);

        var integrationMessage = new IntegrationMessages.ShipmentDeliveryFailed(
            shipment.OrderId,
            command.ShipmentId,
            command.Reason,
            failedAt);

        var outgoing = new OutgoingMessages();
        outgoing.Add(integrationMessage);

        return (events, outgoing);
    }
}
