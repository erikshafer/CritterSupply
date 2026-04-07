using Wolverine.Http;
using Marten;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace Fulfillment.Shipments;

/// <summary>
/// Slice 31: Delivery dispute — first offense reship.
/// Customer disputes a delivered shipment. For first offenses, the resolution is
/// an automatic reshipment with no questions asked.
/// </summary>
public sealed record DisputeDelivery(
    Guid ShipmentId,
    Guid CustomerId);

/// <summary>
/// Slice 31: Delivery dispute handler.
/// Appends DeliveryDisputed, then cascades to CreateReshipment via inline invoke.
/// </summary>
public static class DisputeDeliveryHandler
{
    public static async Task<ProblemDetails> Before(
        DisputeDelivery command,
        IDocumentSession session,
        CancellationToken ct)
    {
        var shipment = await session.LoadAsync<Shipment>(command.ShipmentId, ct);
        if (shipment is null)
            return new ProblemDetails { Detail = "Shipment not found", Status = 404 };

        if (shipment.Status != ShipmentStatus.Delivered)
            return new ProblemDetails
            {
                Detail = $"Cannot dispute delivery for shipment in {shipment.Status} status. Must be Delivered.",
                Status = 400
            };

        return WolverineContinue.NoProblems;
    }

    public static async Task Handle(
        DisputeDelivery command,
        IDocumentSession session,
        IMessageBus bus,
        CancellationToken ct)
    {
        var shipment = await session.LoadAsync<Shipment>(command.ShipmentId, ct);
        if (shipment is null) return;

        var now = DateTimeOffset.UtcNow;

        // TODO: OffenseNumber is a stub (always 1). Tracking per-customer offense history
        // is a Customer Identity / Orders concern, not Fulfillment's.
        var disputed = new DeliveryDisputed(
            command.CustomerId,
            OffenseNumber: 1,
            Resolution: "ReshippedNoQuestionsAsked",
            now);

        session.Events.Append(command.ShipmentId, disputed);

        // Cascade to CreateReshipment — same inline invoke approach as
        // PackingCompleted → GenerateShippingLabel (S2 deviation #1)
        await bus.InvokeAsync(
            new CreateReshipment(command.ShipmentId, "DeliveryDisputed"), ct);
    }
}
