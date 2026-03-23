using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Http;
using Wolverine.Http.Marten;
using Wolverine.Marten;

namespace Returns.Returns;

/// <summary>
/// Warehouse ships the replacement item after original item passes inspection.
/// This handler is called after SubmitInspectionHandler determines inspection passed
/// for an exchange workflow.
///
/// Publishes ExchangeReplacementShipped integration event to:
/// - Customer Experience BC (update UI with tracking number)
/// - Notifications BC (send shipment notification email)
/// </summary>
public sealed record ShipReplacementItem(
    Guid ReturnId,
    string ShipmentId,
    string TrackingNumber);

public sealed class ShipReplacementItemValidator : AbstractValidator<ShipReplacementItem>
{
    public ShipReplacementItemValidator()
    {
        RuleFor(x => x.ReturnId).NotEmpty().WithMessage("ReturnId is required.");
        RuleFor(x => x.ShipmentId).NotEmpty().WithMessage("ShipmentId is required.");
        RuleFor(x => x.TrackingNumber).NotEmpty().WithMessage("TrackingNumber is required.");
    }
}

public static class ShipReplacementItemHandler
{
    public static ProblemDetails Before(ShipReplacementItem command, Return? aggregate)
    {
        if (aggregate is null)
            return new ProblemDetails { Detail = "Return not found.", Status = 404 };

        if (aggregate.Type != ReturnType.Exchange)
            return new ProblemDetails
            {
                Detail = "This return is not an exchange. Only exchanges can ship replacement items.",
                Status = 409
            };

        if (aggregate.Status != ReturnStatus.Inspecting)
            return new ProblemDetails
            {
                Detail = $"Exchange is in '{aggregate.Status}' state. Replacement can only be shipped after inspection passes (Inspecting state).",
                Status = 409
            };

        return WolverineContinue.NoProblems;
    }

    [WolverinePost("/api/returns/{returnId}/ship-replacement")]
    public static (Events, OutgoingMessages) Handle(
        ShipReplacementItem command,
        [WriteAggregate] Return aggregate)
    {
        var now = DateTimeOffset.UtcNow;
        var events = new Events();
        var outgoing = new OutgoingMessages();

        // Append replacement shipped event
        events.Add(new ExchangeReplacementShipped(
            ReturnId: command.ReturnId,
            ShipmentId: command.ShipmentId,
            TrackingNumber: command.TrackingNumber,
            ShippedAt: now));

        // Append exchange completed event (customer receives replacement, workflow complete)
        events.Add(new ExchangeCompleted(
            ReturnId: command.ReturnId,
            PriceDifferenceRefund: aggregate.PriceDifference > 0 ? aggregate.PriceDifference : null,
            CompletedAt: now));

        // Publish replacement shipped integration event
        outgoing.Add(new Messages.Contracts.Returns.ExchangeReplacementShipped(
            ReturnId: command.ReturnId,
            OrderId: aggregate.OrderId,
            CustomerId: aggregate.CustomerId,
            ShipmentId: command.ShipmentId,
            TrackingNumber: command.TrackingNumber,
            ShippedAt: now));

        // Publish exchange completed integration event (Orders BC issues price difference refund)
        outgoing.Add(new Messages.Contracts.Returns.ExchangeCompleted(
            ReturnId: command.ReturnId,
            OrderId: aggregate.OrderId,
            CustomerId: aggregate.CustomerId,
            PriceDifferenceRefund: aggregate.PriceDifference > 0 ? aggregate.PriceDifference : null,
            CompletedAt: now));

        return (events, outgoing);
    }
}
