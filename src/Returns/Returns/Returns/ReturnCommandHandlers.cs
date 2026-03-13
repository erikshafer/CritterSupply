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

/// <summary>
/// CS agent denies a return that is in Requested state.
/// Publishes ReturnDenied integration event with customer-facing message.
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
            CustomerId: aggregate.CustomerId,
            Reason: command.Reason,
            Message: command.Message,
            DeniedAt: now));

        return (denied, outgoing);
    }
}

/// <summary>
/// Warehouse records physical receipt of a return shipment.
/// Publishes ReturnReceived integration event so Customer Experience BC
/// can show "We received your package" — the #1 anxiety-reducer in return flows.
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
    public static (ReturnReceived, OutgoingMessages) Handle(
        ReceiveReturn command,
        [WriteAggregate] Return aggregate)
    {
        var now = DateTimeOffset.UtcNow;
        var domainEvent = new ReturnReceived(
            ReturnId: command.ReturnId,
            ReceivedAt: now);

        var outgoing = new OutgoingMessages();
        outgoing.Add(new Messages.Contracts.Returns.ReturnReceived(
            ReturnId: command.ReturnId,
            OrderId: aggregate.OrderId,
            CustomerId: aggregate.CustomerId,
            ReceivedAt: now));

        return (domainEvent, outgoing);
    }
}

/// <summary>
/// Inspector submits inspection results for a received return.
/// Three-way logic: all-pass (Completed), all-fail (Rejected), or mixed (Completed with partial refund).
/// Publishes appropriate integration events for downstream BCs.
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
                InspectorId: "system", // Phase 2: no inspector auth yet
                StartedAt: now));
        }

        // Partition results into passed and failed
        var passed = command.Results.Where(r => !IsFailedResult(r)).ToList();
        var failed = command.Results.Where(IsFailedResult).ToList();

        // EXCHANGE WORKFLOW: Inspection failure downgrades to rejection (no replacement, no refund)
        if (aggregate.Type == ReturnType.Exchange && failed.Count > 0)
        {
            // Exchange rejected — original item did not pass inspection
            events.Add(new ExchangeRejected(
                ReturnId: command.ReturnId,
                FailureReason: "Item condition does not qualify for exchange. Return rejected.",
                RejectedAt: now));

            outgoing.Add(new Messages.Contracts.Returns.ExchangeRejected(
                ReturnId: command.ReturnId,
                OrderId: aggregate.OrderId,
                CustomerId: aggregate.CustomerId,
                FailureReason: "Item condition does not qualify for exchange. Return rejected.",
                RejectedAt: now));

            return (events, outgoing);
        }

        // EXCHANGE WORKFLOW: Inspection passed — replacement item will be shipped (handled by ShipReplacementItem)
        if (aggregate.Type == ReturnType.Exchange && failed.Count == 0)
        {
            // Exchange inspection passed — transition to ExchangeShipping status
            // Note: ShipReplacementItem handler will append ExchangeReplacementShipped + ExchangeCompleted events
            events.Add(new InspectionPassed(
                ReturnId: command.ReturnId,
                Results: command.Results,
                FinalRefundAmount: aggregate.PriceDifference ?? 0m, // Price difference refund (if any)
                RestockingFeeAmount: 0m, // No restocking fee for exchanges
                CompletedAt: now));

            // For exchanges, we do NOT publish ReturnCompleted here.
            // ShipReplacementItem will publish ExchangeCompleted instead.
            return (events, outgoing);
        }

        // REFUND WORKFLOW: Standard refund logic (all-pass, all-fail, or mixed)
        if (failed.Count == 0)
        {
            // ALL PASSED — full refund
            var (finalRefund, restockingFee) = Return.CalculateEstimatedRefund(aggregate.Items);

            events.Add(new InspectionPassed(
                ReturnId: command.ReturnId,
                Results: command.Results,
                FinalRefundAmount: finalRefund,
                RestockingFeeAmount: restockingFee,
                CompletedAt: now));

            outgoing.Add(new Messages.Contracts.Returns.ReturnCompleted(
                ReturnId: command.ReturnId,
                OrderId: aggregate.OrderId,
                CustomerId: aggregate.CustomerId,
                FinalRefundAmount: finalRefund,
                Items: ToReturnedItems(passed, aggregate.Items, passedInspection: true),
                CompletedAt: now));
        }
        else if (passed.Count == 0)
        {
            // ALL FAILED — no refund
            events.Add(new InspectionFailed(
                ReturnId: command.ReturnId,
                Results: command.Results,
                FailureReason: "Inspection found items in unacceptable condition.",
                CompletedAt: now));

            outgoing.Add(new Messages.Contracts.Returns.ReturnRejected(
                ReturnId: command.ReturnId,
                OrderId: aggregate.OrderId,
                CustomerId: aggregate.CustomerId,
                Reason: "Inspection found items in unacceptable condition.",
                Items: ToReturnedItems(failed, aggregate.Items, passedInspection: false),
                RejectedAt: now));
        }
        else
        {
            // MIXED — partial refund for passed items only
            var passedSkus = passed.Select(p => p.Sku).ToHashSet();
            var passedLineItems = aggregate.Items
                .Where(li => passedSkus.Contains(li.Sku))
                .ToList().AsReadOnly();
            var (partialRefund, restockingFee) = Return.CalculateEstimatedRefund(passedLineItems);

            events.Add(new InspectionMixed(
                ReturnId: command.ReturnId,
                PassedItems: passed.AsReadOnly(),
                FailedItems: failed.AsReadOnly(),
                FinalRefundAmount: partialRefund,
                RestockingFeeAmount: restockingFee,
                CompletedAt: now));

            // Publish ReturnCompleted with ALL items (passed + failed).
            // Passed items have IsRestockable based on inspection; failed items are IsRestockable: false.
            var allItems = ToReturnedItems(passed, aggregate.Items, passedInspection: true)
                .Concat(ToReturnedItems(failed, aggregate.Items, passedInspection: false))
                .ToList().AsReadOnly();

            outgoing.Add(new Messages.Contracts.Returns.ReturnCompleted(
                ReturnId: command.ReturnId,
                OrderId: aggregate.OrderId,
                CustomerId: aggregate.CustomerId,
                FinalRefundAmount: partialRefund,
                Items: allItems,
                CompletedAt: now));
        }

        return (events, outgoing);
    }

    private static bool IsFailedResult(InspectionLineResult r) =>
        r.Condition is ItemCondition.WorseThanExpected ||
        r.Disposition is DispositionDecision.Dispose
            or DispositionDecision.Quarantine
            or DispositionDecision.ReturnToCustomer;

    private static IReadOnlyList<Messages.Contracts.Returns.ReturnedItem> ToReturnedItems(
        List<InspectionLineResult> results,
        IReadOnlyList<ReturnLineItem> originalItems,
        bool passedInspection)
    {
        return results.Select(r =>
        {
            var originalItem = originalItems.FirstOrDefault(li => li.Sku == r.Sku);
            var itemRefund = passedInspection && originalItem is not null
                ? CalculateItemRefund(originalItem)
                : (decimal?)null;

            return new Messages.Contracts.Returns.ReturnedItem(
                Sku: r.Sku,
                Quantity: r.Quantity,
                IsRestockable: passedInspection && r.IsRestockable,
                WarehouseId: r.WarehouseLocation,
                RestockCondition: passedInspection
                    ? r.Condition switch
                    {
                        ItemCondition.AsExpected => "LikeNew",
                        ItemCondition.BetterThanExpected => "New",
                        ItemCondition.WorseThanExpected => "Opened",
                        _ => "Opened"
                    }
                    : null,
                RefundAmount: itemRefund,
                RejectionReason: passedInspection ? null : "Item condition did not meet return requirements.");
        }).ToList().AsReadOnly();
    }

    private static decimal CalculateItemRefund(ReturnLineItem item)
    {
        const decimal restockingFeeRate = 0.15m;
        var isFeeExempt = item.Reason is ReturnReason.Defective
            or ReturnReason.WrongItem or ReturnReason.DamagedInTransit;
        var fee = isFeeExempt ? 0m : Math.Round(item.LineTotal * restockingFeeRate, 2);
        return item.LineTotal - fee;
    }
}

/// <summary>
/// Scheduled command that fires when an approved return is never shipped.
/// Only expires if the return is still in Approved state (no-op if already transitioned).
/// Publishes ReturnExpired integration event for Notifications BC and Orders saga.
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

        // Publish integration event for Notifications BC and Orders saga
        await bus.PublishAsync(new Messages.Contracts.Returns.ReturnExpired(
            ReturnId: command.ReturnId,
            OrderId: aggregate.OrderId,
            CustomerId: aggregate.CustomerId,
            ExpiredAt: expired.ExpiredAt));
    }
}
