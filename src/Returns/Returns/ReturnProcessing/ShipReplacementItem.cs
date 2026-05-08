using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;

namespace Returns.ReturnProcessing;

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
    [Authorize]
    public static (Events, OutgoingMessages) Handle(
        ShipReplacementItem command,
        [WriteAggregate] Return aggregate)
    {
        var now = DateTimeOffset.UtcNow;
        var events = new Events();
        var outgoing = new OutgoingMessages();

        // Cross-product exchange where the replacement is cheaper than the original means
        // the customer is owed a partial refund. PriceDifference is positive in that direction
        // (set by ApproveExchangeHandler: priceDifference = originalTotal - replacementTotal).
        var partialRefundAmount = aggregate.PriceDifference > 0 ? aggregate.PriceDifference : null;

        // Append replacement shipped event
        events.Add(new ExchangeReplacementShipped(
            ReturnId: command.ReturnId,
            ShipmentId: command.ShipmentId,
            TrackingNumber: command.TrackingNumber,
            ShippedAt: now));

        // Append exchange completed event (customer receives replacement, workflow complete)
        events.Add(new ExchangeCompleted(
            ReturnId: command.ReturnId,
            PriceDifferenceRefund: partialRefundAmount,
            CompletedAt: now));

        // Append partial refund issued domain event when there is a refund owed.
        // Mirrors the ExchangeAdditionalPaymentRequired domain event already emitted by
        // ApproveExchangeHandler when the replacement costs more.
        if (partialRefundAmount.HasValue)
        {
            events.Add(new ExchangePartialRefundIssued(
                ReturnId: command.ReturnId,
                RefundAmount: partialRefundAmount.Value,
                IssuedAt: now));
        }

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
            PriceDifferenceRefund: partialRefundAmount,
            CompletedAt: now));

        // Publish partial refund integration event so Payments BC can actually issue the refund and
        // Storefront can surface it. Closes the M45.1 / S3 "contract registered for publication but
        // never constructed" anti-pattern. Only emitted when there is an actual refund owed.
        if (partialRefundAmount.HasValue)
        {
            outgoing.Add(new Messages.Contracts.Returns.ExchangePartialRefundIssued(
                ReturnId: command.ReturnId,
                OrderId: aggregate.OrderId,
                CustomerId: aggregate.CustomerId,
                RefundAmount: partialRefundAmount.Value,
                IssuedAt: now));
        }

        return (events, outgoing);
    }
}
