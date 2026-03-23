using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Http;
using Wolverine.Http.Marten;
using Wolverine.Marten;

namespace Returns.ReturnProcessing;

// ---------------------------------------------------------------------------
// Command
// ---------------------------------------------------------------------------

public sealed record SubmitInspection(
    Guid ReturnId,
    IReadOnlyList<InspectionLineResult> Results);

// ---------------------------------------------------------------------------
// Validator
// ---------------------------------------------------------------------------

public sealed class SubmitInspectionValidator : AbstractValidator<SubmitInspection>
{
    public SubmitInspectionValidator()
    {
        RuleFor(x => x.ReturnId).NotEmpty().WithMessage("ReturnId is required.");
        RuleFor(x => x.Results).NotEmpty().WithMessage("At least one inspection result is required.");

        RuleForEach(x => x.Results).ChildRules(result =>
        {
            result.RuleFor(r => r.Sku).NotEmpty().WithMessage("SKU is required.");
            result.RuleFor(r => r.Quantity).GreaterThan(0).WithMessage("Quantity must be greater than zero.");
            result.RuleFor(r => r.Condition).IsInEnum().WithMessage("Item condition is invalid.");
            result.RuleFor(r => r.Disposition).IsInEnum().WithMessage("Disposition decision is invalid.");
        });
    }
}

// ---------------------------------------------------------------------------
// Handler
// ---------------------------------------------------------------------------

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
