using Marten;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Http;
using Wolverine.Http.Marten;
using Wolverine.Marten;

namespace Returns.Returns;

/// <summary>
/// CS agent approves a return that is in Requested state (manual review required).
/// </summary>
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
    public static async Task<ReturnApproved> Handle(
        ApproveReturn command,
        [WriteAggregate] Return aggregate,
        IMessageBus bus)
    {
        var now = DateTimeOffset.UtcNow;
        var shipByDeadline = now.AddDays(ReturnEligibilityWindow.ReturnWindowDays);

        var (estimatedRefund, restockingFee) = Return.CalculateEstimatedRefund(aggregate.Items);

        // Schedule expiration
        await bus.ScheduleAsync(new ExpireReturn(command.ReturnId), shipByDeadline);

        return new ReturnApproved(
            ReturnId: command.ReturnId,
            EstimatedRefundAmount: estimatedRefund,
            RestockingFeeAmount: restockingFee,
            ShipByDeadline: shipByDeadline,
            ApprovedAt: now);
    }
}

/// <summary>
/// CS agent denies a return that is in Requested state.
/// </summary>
public static class DenyReturnHandler
{
    public static ProblemDetails Before(DenyReturn command, Return? aggregate)
    {
        if (aggregate is null)
            return new ProblemDetails { Detail = "Return not found.", Status = 404 };

        if (aggregate.Status != ReturnStatus.Requested)
            return new ProblemDetails
            {
                Detail = $"Return is in '{aggregate.Status}' state and cannot be denied. Only returns in 'Requested' state can be denied.",
                Status = 409
            };

        return WolverineContinue.NoProblems;
    }

    [WolverinePost("/api/returns/{returnId}/deny")]
    public static (ReturnDenied, OutgoingMessages) Handle(
        DenyReturn command,
        [WriteAggregate] Return aggregate)
    {
        var now = DateTimeOffset.UtcNow;
        var denied = new ReturnDenied(
            ReturnId: command.ReturnId,
            Reason: command.Reason,
            Message: command.Message,
            DeniedAt: now);

        var outgoing = new OutgoingMessages();
        outgoing.Add(new Messages.Contracts.Returns.ReturnDenied(
            ReturnId: command.ReturnId,
            OrderId: aggregate.OrderId,
            Reason: command.Reason,
            DeniedAt: now));

        return (denied, outgoing);
    }
}

/// <summary>
/// Warehouse records physical receipt of a return shipment.
/// </summary>
public static class ReceiveReturnHandler
{
    public static ProblemDetails Before(ReceiveReturn command, Return? aggregate)
    {
        if (aggregate is null)
            return new ProblemDetails { Detail = "Return not found.", Status = 404 };

        if (aggregate.Status != ReturnStatus.Approved)
            return new ProblemDetails
            {
                Detail = $"Return must be in 'Approved' state to be received. Current state: '{aggregate.Status}'.",
                Status = 409
            };

        return WolverineContinue.NoProblems;
    }

    [WolverinePost("/api/returns/{returnId}/receive")]
    public static ReturnReceived Handle(
        ReceiveReturn command,
        [WriteAggregate] Return aggregate)
    {
        return new ReturnReceived(
            ReturnId: command.ReturnId,
            ReceivedAt: DateTimeOffset.UtcNow);
    }
}

/// <summary>
/// Inspector submits inspection results for a received return.
/// Determines whether inspection passes (Completed) or fails (Rejected).
/// </summary>
public static class SubmitInspectionHandler
{
    public static ProblemDetails Before(SubmitInspection command, Return? aggregate)
    {
        if (aggregate is null)
            return new ProblemDetails { Detail = "Return not found.", Status = 404 };

        if (aggregate.Status is not (ReturnStatus.Received or ReturnStatus.Inspecting))
            return new ProblemDetails
            {
                Detail = $"Return must be in 'Received' or 'Inspecting' state to submit inspection. Current state: '{aggregate.Status}'.",
                Status = 409
            };

        return WolverineContinue.NoProblems;
    }

    [WolverinePost("/api/returns/{returnId}/inspection")]
    public static (Events, OutgoingMessages) Handle(
        SubmitInspection command,
        [WriteAggregate] Return aggregate)
    {
        var now = DateTimeOffset.UtcNow;
        var events = new Events();
        var outgoing = new OutgoingMessages();

        // If not already inspecting, start inspection
        if (aggregate.Status == ReturnStatus.Received)
        {
            events.Add(new InspectionStarted(
                ReturnId: command.ReturnId,
                InspectorId: "system", // Phase 1: no inspector auth
                StartedAt: now));
        }

        // Determine pass/fail based on disposition
        var hasFailures = command.Results.Any(r =>
            r.Condition == ItemCondition.WorseThanExpected ||
            r.Disposition is DispositionDecision.Dispose
                or DispositionDecision.Quarantine
                or DispositionDecision.ReturnToCustomer);

        if (!hasFailures)
        {
            // Inspection passed — calculate final refund
            var (finalRefund, restockingFee) = Return.CalculateEstimatedRefund(aggregate.Items);

            events.Add(new InspectionPassed(
                ReturnId: command.ReturnId,
                Results: command.Results,
                FinalRefundAmount: finalRefund,
                RestockingFeeAmount: restockingFee,
                CompletedAt: now));

            // Publish ReturnCompleted integration event
            outgoing.Add(new Messages.Contracts.Returns.ReturnCompleted(
                ReturnId: command.ReturnId,
                OrderId: aggregate.OrderId,
                FinalRefundAmount: finalRefund,
                CompletedAt: now));
        }
        else
        {
            // Inspection failed
            events.Add(new InspectionFailed(
                ReturnId: command.ReturnId,
                Results: command.Results,
                FailureReason: "Inspection found items in unacceptable condition.",
                CompletedAt: now));
        }

        return (events, outgoing);
    }
}

/// <summary>
/// Scheduled command that fires when an approved return is never shipped.
/// Only expires if the return is still in Approved state (no-op if already transitioned).
/// </summary>
public static class ExpireReturnHandler
{
    public static async Task Handle(
        ExpireReturn command,
        IDocumentSession session,
        IMessageBus bus,
        CancellationToken ct)
    {
        var aggregate = await session.Events.AggregateStreamAsync<Return>(command.ReturnId, token: ct);

        // No-op if already past Approved state (customer shipped, or CS intervened)
        if (aggregate is null || aggregate.Status != ReturnStatus.Approved)
            return;

        var expired = new ReturnExpired(
            ReturnId: command.ReturnId,
            ExpiredAt: DateTimeOffset.UtcNow);

        session.Events.Append(command.ReturnId, expired);
    }
}
