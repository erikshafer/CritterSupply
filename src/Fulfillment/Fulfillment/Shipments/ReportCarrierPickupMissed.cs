using FluentValidation;
using Marten;
using Microsoft.AspNetCore.Mvc;
using Wolverine.Http;

namespace Fulfillment.Shipments;

/// <summary>
/// Slice 23: Report that a carrier missed the scheduled pickup window.
/// </summary>
public sealed record ReportCarrierPickupMissed(
    Guid ShipmentId,
    string Carrier,
    string PickupWindow)
{
    public sealed class Validator : AbstractValidator<ReportCarrierPickupMissed>
    {
        public Validator()
        {
            RuleFor(x => x.ShipmentId).NotEmpty();
            RuleFor(x => x.Carrier).NotEmpty().MaximumLength(100);
            RuleFor(x => x.PickupWindow).NotEmpty().MaximumLength(50);
        }
    }
}

/// <summary>
/// Handler for reporting a missed carrier pickup.
/// Appends CarrierPickupMissed and CarrierRelationsEscalated events.
/// </summary>
public static class ReportCarrierPickupMissedHandler
{
    public static async Task<ProblemDetails> Before(
        ReportCarrierPickupMissed command,
        IDocumentSession session,
        CancellationToken ct)
    {
        var shipment = await session.LoadAsync<Shipment>(command.ShipmentId, ct);
        if (shipment is null)
            return new ProblemDetails { Detail = "Shipment not found", Status = 404 };

        if (shipment.Status is not (ShipmentStatus.Staged or ShipmentStatus.Labeled))
            return new ProblemDetails
            {
                Detail = $"Cannot report missed pickup for shipment in {shipment.Status} status",
                Status = 400
            };

        return WolverineContinue.NoProblems;
    }

    public static void Handle(ReportCarrierPickupMissed command, IDocumentSession session)
    {
        var now = DateTimeOffset.UtcNow;
        session.Events.Append(command.ShipmentId,
            new CarrierPickupMissed(command.Carrier, command.PickupWindow, now),
            new CarrierRelationsEscalated(command.Carrier, $"Missed pickup window: {command.PickupWindow}", now));
    }
}
