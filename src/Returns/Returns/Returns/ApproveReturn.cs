using FluentValidation;
using Marten;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Http;
using Wolverine.Http.Marten;
using Wolverine.Marten;

namespace Returns.Returns;

/// <summary>
/// CS agent approves a return that is in Requested state (manual review required).
/// Publishes ReturnApproved integration event for Customer Experience BC and Notifications BC.
/// </summary>
public sealed record ApproveReturn(Guid ReturnId);

public sealed class ApproveReturnValidator : AbstractValidator<ApproveReturn>
{
    public ApproveReturnValidator()
    {
        RuleFor(x => x.ReturnId).NotEmpty().WithMessage("ReturnId is required.");
    }
}

public static class ApproveReturnHandler
{
    public static ProblemDetails Before(ApproveReturn command, Return? aggregate)
    {
        if (aggregate is null)
            return new ProblemDetails { Detail = "Return not found.", Status = 404 };

        if (aggregate.Status != ReturnStatus.Requested)
            return new ProblemDetails
            {
                Detail = $"Return is in '{aggregate.Status}' state and cannot be approved. Only returns in 'Requested' state can be approved.",
                Status = 409
            };

        return WolverineContinue.NoProblems;
    }

    [WolverinePost("/api/returns/{returnId}/approve")]
    public static async Task<(ReturnApproved, OutgoingMessages)> Handle(
        ApproveReturn command,
        [WriteAggregate] Return aggregate,
        IMessageBus bus)
    {
        var now = DateTimeOffset.UtcNow;
        var shipByDeadline = now.AddDays(ReturnEligibilityWindow.ReturnWindowDays);

        var (estimatedRefund, restockingFee) = Return.CalculateEstimatedRefund(aggregate.Items);

        // Schedule expiration
        await bus.ScheduleAsync(new ExpireReturn(command.ReturnId), shipByDeadline);

        var domainEvent = new ReturnApproved(
            ReturnId: command.ReturnId,
            EstimatedRefundAmount: estimatedRefund,
            RestockingFeeAmount: restockingFee,
            ShipByDeadline: shipByDeadline,
            ApprovedAt: now);

        var outgoing = new OutgoingMessages();
        outgoing.Add(new Messages.Contracts.Returns.ReturnApproved(
            ReturnId: command.ReturnId,
            OrderId: aggregate.OrderId,
            CustomerId: aggregate.CustomerId,
            EstimatedRefundAmount: estimatedRefund,
            RestockingFeeAmount: restockingFee,
            ShipByDeadline: shipByDeadline,
            ApprovedAt: now));

        return (domainEvent, outgoing);
    }
}
