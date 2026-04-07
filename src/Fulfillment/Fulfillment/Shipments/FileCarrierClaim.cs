using Wolverine.Http;
using Marten;
using Microsoft.AspNetCore.Mvc;

namespace Fulfillment.Shipments;

/// <summary>
/// Slice 33: Carrier claim filing command.
/// Filed when a shipment is lost and the operations team wants to claim
/// against the carrier's insurance.
/// </summary>
public sealed record FileCarrierClaim(
    Guid ShipmentId,
    string Carrier,
    string ClaimType);

/// <summary>
/// Slice 33: Carrier claim filing handler.
/// Appends CarrierClaimFiled to the Shipment stream.
/// No integration events — this is an internal operations action.
/// </summary>
public static class FileCarrierClaimHandler
{
    public static async Task<ProblemDetails> Before(
        FileCarrierClaim command,
        IDocumentSession session,
        CancellationToken ct)
    {
        var shipment = await session.LoadAsync<Shipment>(command.ShipmentId, ct);
        if (shipment is null)
            return new ProblemDetails { Detail = "Shipment not found", Status = 404 };

        if (shipment.Status is not (ShipmentStatus.LostInTransit or ShipmentStatus.LostReplacementShipped))
            return new ProblemDetails
            {
                Detail = $"Cannot file carrier claim for shipment in {shipment.Status} status. " +
                         "Must be LostInTransit or LostReplacementShipped.",
                Status = 400
            };

        return WolverineContinue.NoProblems;
    }

    public static async Task Handle(
        FileCarrierClaim command,
        IDocumentSession session,
        CancellationToken ct)
    {
        var shipment = await session.LoadAsync<Shipment>(command.ShipmentId, ct);
        if (shipment is null) return;

        session.Events.Append(command.ShipmentId,
            new CarrierClaimFiled(
                command.Carrier,
                command.ClaimType,
                command.ShipmentId,
                shipment.TrackingNumber,
                DateTimeOffset.UtcNow));
    }
}
