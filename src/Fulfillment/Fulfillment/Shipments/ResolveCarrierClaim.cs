using Wolverine.Http;
using Marten;
using Microsoft.AspNetCore.Mvc;

namespace Fulfillment.Shipments;

/// <summary>
/// Slice 34: Carrier claim resolution command.
/// Triggered when a carrier responds to a claim (paid, denied, or settled).
/// </summary>
public sealed record ResolveCarrierClaim(
    Guid ShipmentId,
    string Resolution,
    decimal? AmountUSD);

/// <summary>
/// Slice 34: Carrier claim resolution handler.
/// Appends CarrierClaimResolved to the Shipment stream.
/// </summary>
public static class ResolveCarrierClaimHandler
{
    public static async Task<ProblemDetails> Before(
        ResolveCarrierClaim command,
        IDocumentSession session,
        CancellationToken ct)
    {
        var shipment = await session.LoadAsync<Shipment>(command.ShipmentId, ct);
        if (shipment is null)
            return new ProblemDetails { Detail = "Shipment not found", Status = 404 };

        // Precondition: CarrierClaimFiled must have been appended
        var events = await session.Events.FetchStreamAsync(command.ShipmentId, token: ct);
        var hasClaim = events.Any(e => e.Data is CarrierClaimFiled);
        if (!hasClaim)
            return new ProblemDetails
            {
                Detail = "Cannot resolve carrier claim — no claim has been filed for this shipment.",
                Status = 400
            };

        return WolverineContinue.NoProblems;
    }

    public static void Handle(
        ResolveCarrierClaim command,
        IDocumentSession session)
    {
        session.Events.Append(command.ShipmentId,
            new CarrierClaimResolved(
                command.Resolution,
                command.AmountUSD,
                DateTimeOffset.UtcNow));
    }
}
