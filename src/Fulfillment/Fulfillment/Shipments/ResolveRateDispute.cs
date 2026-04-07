using Wolverine.Http;
using Marten;
using Microsoft.AspNetCore.Mvc;

namespace Fulfillment.Shipments;

/// <summary>
/// Slice 38: Rate dispute resolution command.
/// Carrier responds to the rate dispute.
/// </summary>
public sealed record ResolveRateDispute(
    Guid ShipmentId,
    string DisputeId,
    string Resolution,
    decimal? AdjustedAmountUSD);

/// <summary>
/// Slice 38: Rate dispute resolution handler.
/// </summary>
public static class ResolveRateDisputeHandler
{
    public static async Task<ProblemDetails> Before(
        ResolveRateDispute command,
        IDocumentSession session,
        CancellationToken ct)
    {
        var shipment = await session.LoadAsync<Shipment>(command.ShipmentId, ct);
        if (shipment is null)
            return new ProblemDetails { Detail = "Shipment not found", Status = 404 };

        // Precondition: RateDisputeRaised must have been appended
        var events = await session.Events.FetchStreamAsync(command.ShipmentId, token: ct);
        var hasDispute = events.Any(e => e.Data is RateDisputeRaised rd && rd.DisputeId == command.DisputeId);
        if (!hasDispute)
            return new ProblemDetails
            {
                Detail = $"Cannot resolve rate dispute — no dispute with ID {command.DisputeId} found.",
                Status = 400
            };

        return WolverineContinue.NoProblems;
    }

    public static void Handle(
        ResolveRateDispute command,
        IDocumentSession session)
    {
        session.Events.Append(command.ShipmentId,
            new RateDisputeResolved(
                command.DisputeId,
                command.Resolution,
                command.AdjustedAmountUSD,
                DateTimeOffset.UtcNow));
    }
}
