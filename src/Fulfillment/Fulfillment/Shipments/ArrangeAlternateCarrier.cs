using FluentValidation;
using Marten;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Http;
using IntegrationMessages = Messages.Contracts.Fulfillment;

namespace Fulfillment.Shipments;

/// <summary>
/// Slice 23: Arrange an alternate carrier after a missed pickup.
/// </summary>
public sealed record ArrangeAlternateCarrier(
    Guid ShipmentId,
    string NewCarrier,
    string NewService)
{
    public sealed class Validator : AbstractValidator<ArrangeAlternateCarrier>
    {
        public Validator()
        {
            RuleFor(x => x.ShipmentId).NotEmpty();
            RuleFor(x => x.NewCarrier).NotEmpty().MaximumLength(100);
            RuleFor(x => x.NewService).NotEmpty().MaximumLength(100);
        }
    }
}

/// <summary>
/// Handler for arranging an alternate carrier after a missed pickup.
/// Voids the original label, generates a new one, and assigns a new tracking number.
/// </summary>
public static class ArrangeAlternateCarrierHandler
{
    public static async Task<ProblemDetails> Before(
        ArrangeAlternateCarrier command,
        IDocumentSession session,
        CancellationToken ct)
    {
        var shipment = await session.LoadAsync<Shipment>(command.ShipmentId, ct);
        if (shipment is null)
            return new ProblemDetails { Detail = "Shipment not found", Status = 404 };

        if (shipment.Carrier is null)
            return new ProblemDetails
            {
                Detail = "Shipment has no carrier assigned to replace",
                Status = 400
            };

        return WolverineContinue.NoProblems;
    }

    public static async Task Handle(
        ArrangeAlternateCarrier command,
        IDocumentSession session,
        IMessageBus bus,
        CancellationToken ct)
    {
        var shipment = await session.LoadAsync<Shipment>(command.ShipmentId, ct);
        if (shipment is null) return;

        var now = DateTimeOffset.UtcNow;
        var originalCarrier = shipment.Carrier ?? "Unknown";

        // 1. Arrange alternate carrier
        session.Events.Append(command.ShipmentId,
            new AlternateCarrierArranged(originalCarrier, command.NewCarrier, now));

        // 2. Void the original label
        session.Events.Append(command.ShipmentId,
            new ShippingLabelVoided(originalCarrier, "Carrier changed after missed pickup", now));

        // 3. Generate new label and tracking number
        var trackingNumber = $"1Z{command.NewCarrier.ToUpperInvariant()[..3]}{Guid.NewGuid():N}"[..24];

        session.Events.Append(command.ShipmentId,
            new ShippingLabelGenerated(command.NewCarrier, command.NewService, 0m, null, now),
            new TrackingNumberAssigned(trackingNumber, command.NewCarrier, now));

        // Publish TrackingNumberAssigned with updated tracking for Orders saga
        await bus.PublishAsync(new IntegrationMessages.TrackingNumberAssigned(
            shipment.OrderId,
            command.ShipmentId,
            trackingNumber,
            command.NewCarrier,
            now));
    }
}
