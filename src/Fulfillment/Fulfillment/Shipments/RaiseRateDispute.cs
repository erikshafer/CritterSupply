using Wolverine.Http;
using Marten;
using Microsoft.AspNetCore.Mvc;

namespace Fulfillment.Shipments;

/// <summary>
/// Slice 38: Rate dispute raise command.
/// Filed when a carrier bills at a different weight than the shipment's actual weight.
/// </summary>
public sealed record RaiseRateDispute(
    Guid ShipmentId,
    string DisputeId,
    decimal OriginalBillableWeight,
    decimal ClaimedWeight,
    string Carrier);

/// <summary>
/// Slice 38: Rate dispute raise handler.
/// </summary>
public static class RaiseRateDisputeHandler
{
    public static async Task<ProblemDetails> Before(
        RaiseRateDispute command,
        IDocumentSession session,
        CancellationToken ct)
    {
        var shipment = await session.LoadAsync<Shipment>(command.ShipmentId, ct);
        if (shipment is null)
            return new ProblemDetails { Detail = "Shipment not found", Status = 404 };

        if (shipment.Status is not (ShipmentStatus.Delivered or ShipmentStatus.LostReplacementShipped))
            return new ProblemDetails
            {
                Detail = $"Cannot raise rate dispute for shipment in {shipment.Status} status. " +
                         "Must be Delivered or LostReplacementShipped.",
                Status = 400
            };

        return WolverineContinue.NoProblems;
    }

    public static void Handle(
        RaiseRateDispute command,
        IDocumentSession session)
    {
        session.Events.Append(command.ShipmentId,
            new RateDisputeRaised(
                command.DisputeId,
                command.OriginalBillableWeight,
                command.ClaimedWeight,
                command.Carrier,
                DateTimeOffset.UtcNow));
    }
}
