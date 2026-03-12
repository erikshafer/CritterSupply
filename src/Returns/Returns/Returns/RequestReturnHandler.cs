using Marten;
using Wolverine;
using Wolverine.Http;

namespace Returns.Returns;

/// <summary>
/// Handles RequestReturn command — creates a new Return aggregate stream.
/// Validates eligibility (window, returnable items) and auto-approves
/// Defective/WrongItem/DamagedInTransit/Unwanted reasons. Only "Other" goes to CS review.
/// </summary>
public static class RequestReturnHandler
{
    [WolverinePost("/api/returns")]
    public static async Task<RequestReturnResponse> Handle(
        RequestReturn command,
        IDocumentSession session,
        IMessageBus bus,
        CancellationToken ct)
    {
        // Look up eligibility window for this order
        var eligibility = await session
            .LoadAsync<ReturnEligibilityWindow>(command.OrderId, ct);

        if (eligibility is null)
        {
            return RequestReturnResponse.Denied(
                command.OrderId,
                "OrderNotDelivered",
                "This order has not been delivered yet or is not eligible for returns.");
        }

        if (eligibility.IsExpired)
        {
            return RequestReturnResponse.Denied(
                command.OrderId,
                "OutsideReturnWindow",
                $"Your order was delivered more than {ReturnEligibilityWindow.ReturnWindowDays} days ago and is no longer eligible for return.");
        }

        // Map command items to return line items
        var items = command.Items.Select(i => new ReturnLineItem(
            Sku: i.Sku,
            ProductName: i.ProductName,
            Quantity: i.Quantity,
            UnitPrice: i.UnitPrice,
            LineTotal: i.UnitPrice * i.Quantity,
            Reason: i.Reason,
            Explanation: i.Explanation
        )).ToList().AsReadOnly();

        var returnId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;

        // Create the return stream with ReturnRequested event
        var requested = new ReturnRequested(
            ReturnId: returnId,
            OrderId: command.OrderId,
            CustomerId: command.CustomerId,
            Items: items,
            RequestedAt: now);

        session.Events.StartStream<Return>(returnId, requested);

        // Determine auto-approval
        var allReasons = items.Select(i => i.Reason).Distinct().ToList();
        var requiresReview = allReasons.Any(r => r == ReturnReason.Other);

        var (estimatedRefund, restockingFee) = Return.CalculateEstimatedRefund(items);

        if (!requiresReview)
        {
            // Auto-approve: Defective, WrongItem, DamagedInTransit, Unwanted
            var shipByDeadline = now.AddDays(ReturnEligibilityWindow.ReturnWindowDays);
            var approved = new ReturnApproved(
                ReturnId: returnId,
                EstimatedRefundAmount: estimatedRefund,
                RestockingFeeAmount: restockingFee,
                ShipByDeadline: shipByDeadline,
                ApprovedAt: now);

            session.Events.Append(returnId, approved);

            // Schedule expiration
            await bus.ScheduleAsync(new ExpireReturn(returnId), shipByDeadline);

            // Publish integration event
            await bus.PublishAsync(new Messages.Contracts.Returns.ReturnRequested(
                ReturnId: returnId,
                OrderId: command.OrderId,
                CustomerId: command.CustomerId,
                RequestedAt: now));

            return RequestReturnResponse.Approved(
                returnId, command.OrderId, items, estimatedRefund,
                restockingFee, shipByDeadline, now);
        }

        // Requires CS review — publish integration event but stay in Requested state
        await bus.PublishAsync(new Messages.Contracts.Returns.ReturnRequested(
            ReturnId: returnId,
            OrderId: command.OrderId,
            CustomerId: command.CustomerId,
            RequestedAt: now));

        return RequestReturnResponse.UnderReview(
            returnId, command.OrderId, items, estimatedRefund, restockingFee, now);
    }
}

public sealed record RequestReturnResponse(
    Guid? ReturnId,
    Guid OrderId,
    string Status,
    string? DenialReason,
    string? DenialMessage,
    IReadOnlyList<ReturnLineItemResponse>? Items,
    decimal TotalRestockingFee,
    decimal EstimatedTotalRefund,
    DateTimeOffset? ShipByDate,
    DateTimeOffset RequestedAt)
{
    public static RequestReturnResponse Approved(
        Guid returnId, Guid orderId, IReadOnlyList<ReturnLineItem> items,
        decimal estimatedRefund, decimal restockingFee,
        DateTimeOffset shipByDate, DateTimeOffset requestedAt) =>
        new(returnId, orderId, "Approved", null, null,
            items.Select(ReturnLineItemResponse.From).ToList().AsReadOnly(),
            restockingFee, estimatedRefund, shipByDate, requestedAt);

    public static RequestReturnResponse UnderReview(
        Guid returnId, Guid orderId, IReadOnlyList<ReturnLineItem> items,
        decimal estimatedRefund, decimal restockingFee, DateTimeOffset requestedAt) =>
        new(returnId, orderId, "UnderReview", null, null,
            items.Select(ReturnLineItemResponse.From).ToList().AsReadOnly(),
            restockingFee, estimatedRefund, null, requestedAt);

    public static RequestReturnResponse Denied(
        Guid orderId, string reason, string message) =>
        new(null, orderId, "Denied", reason, message, null, 0, 0, null, DateTimeOffset.UtcNow);
}

public sealed record ReturnLineItemResponse(
    string Sku,
    string ProductName,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal,
    string ReturnReason,
    decimal RestockingFeeRate,
    decimal RestockingFeeAmount,
    decimal EstimatedRefund)
{
    private const decimal FeeRate = 0.15m;

    public static ReturnLineItemResponse From(ReturnLineItem item)
    {
        var isFeeExempt = item.Reason is Returns.ReturnReason.Defective
            or Returns.ReturnReason.WrongItem or Returns.ReturnReason.DamagedInTransit;
        var feeRate = isFeeExempt ? 0m : FeeRate;
        var feeAmount = Math.Round(item.LineTotal * feeRate, 2);

        return new ReturnLineItemResponse(
            Sku: item.Sku,
            ProductName: item.ProductName,
            Quantity: item.Quantity,
            UnitPrice: item.UnitPrice,
            LineTotal: item.LineTotal,
            ReturnReason: item.Reason.ToString(),
            RestockingFeeRate: feeRate,
            RestockingFeeAmount: feeAmount,
            EstimatedRefund: item.LineTotal - feeAmount);
    }
}
