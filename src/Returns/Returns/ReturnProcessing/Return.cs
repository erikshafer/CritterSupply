namespace Returns.ReturnProcessing;

/// <summary>
/// Event-sourced aggregate representing a customer return request.
/// Lifecycle: Requested → Approved/Denied → Received → Inspecting → Completed/Rejected/Expired
/// Exchange Lifecycle: Requested → Approved/Denied → Received → Inspecting → ExchangeShipping → ExchangeCompleted/Rejected
/// </summary>
public sealed record Return(
    Guid Id,
    Guid OrderId,
    Guid CustomerId,
    IReadOnlyList<ReturnLineItem> Items,
    ReturnStatus Status,
    ReturnType Type,
    decimal EstimatedRefundAmount,
    decimal RestockingFeeAmount,
    DateTimeOffset? ShipByDeadline,
    string? DenialReason,
    string? DenialMessage,
    string? InspectorId,
    IReadOnlyList<InspectionLineResult>? InspectionResults,
    decimal? FinalRefundAmount,
    ExchangeRequest? ExchangeRequest,
    string? ReplacementShipmentId,
    decimal? PriceDifference,
    DateTimeOffset RequestedAt,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? DeniedAt,
    DateTimeOffset? ReceivedAt,
    DateTimeOffset? InspectionStartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? ExpiredAt,
    DateTimeOffset? ReplacementShippedAt)
{
    // Terminal states — no further transitions allowed
    public bool IsTerminal => Status is ReturnStatus.Denied
        or ReturnStatus.Completed or ReturnStatus.Rejected or ReturnStatus.Expired;

    public static Return Create(ReturnRequested @event)
    {
        var (estimatedRefund, restockingFee) = CalculateEstimatedRefund(@event.Items);

        return new Return(
            Id: @event.ReturnId,
            OrderId: @event.OrderId,
            CustomerId: @event.CustomerId,
            Items: @event.Items,
            Status: ReturnStatus.Requested,
            Type: @event.Type,
            EstimatedRefundAmount: estimatedRefund,
            RestockingFeeAmount: restockingFee,
            ShipByDeadline: null,
            DenialReason: null,
            DenialMessage: null,
            InspectorId: null,
            InspectionResults: null,
            FinalRefundAmount: null,
            ExchangeRequest: @event.ExchangeRequest,
            ReplacementShipmentId: null,
            PriceDifference: null,
            RequestedAt: @event.RequestedAt,
            ApprovedAt: null,
            DeniedAt: null,
            ReceivedAt: null,
            InspectionStartedAt: null,
            CompletedAt: null,
            ExpiredAt: null,
            ReplacementShippedAt: null);
    }

    public Return Apply(ReturnApproved @event) => this with
    {
        Status = ReturnStatus.Approved,
        EstimatedRefundAmount = @event.EstimatedRefundAmount,
        RestockingFeeAmount = @event.RestockingFeeAmount,
        ShipByDeadline = @event.ShipByDeadline,
        ApprovedAt = @event.ApprovedAt
    };

    public Return Apply(ReturnDenied @event) => this with
    {
        Status = ReturnStatus.Denied,
        DenialReason = @event.Reason,
        DenialMessage = @event.Message,
        DeniedAt = @event.DeniedAt
    };

    public Return Apply(ReturnReceived @event) => this with
    {
        Status = ReturnStatus.Received,
        ReceivedAt = @event.ReceivedAt
    };

    public Return Apply(InspectionStarted @event) => this with
    {
        Status = ReturnStatus.Inspecting,
        InspectorId = @event.InspectorId,
        InspectionStartedAt = @event.StartedAt
    };

    public Return Apply(InspectionPassed @event)
    {
        // For exchanges, inspection passed means we stay in Inspecting state
        // (waiting for ShipReplacementItem). For refunds, inspection passed means completed.
        var newStatus = Type == ReturnType.Exchange
            ? ReturnStatus.Inspecting  // Exchange: ready for replacement shipment
            : ReturnStatus.Completed;   // Refund: completed

        return this with
        {
            Status = newStatus,
            InspectionResults = @event.Results,
            FinalRefundAmount = @event.FinalRefundAmount,
            RestockingFeeAmount = @event.RestockingFeeAmount,
            CompletedAt = Type == ReturnType.Refund ? @event.CompletedAt : null // Only set for refunds
        };
    }

    public Return Apply(InspectionFailed @event) => this with
    {
        Status = ReturnStatus.Rejected,
        InspectionResults = @event.Results,
        CompletedAt = @event.CompletedAt
    };

    public Return Apply(InspectionMixed @event) => this with
    {
        Status = ReturnStatus.Completed,
        InspectionResults = @event.PassedItems.Concat(@event.FailedItems).ToList().AsReadOnly(),
        FinalRefundAmount = @event.FinalRefundAmount,
        RestockingFeeAmount = @event.RestockingFeeAmount,
        CompletedAt = @event.CompletedAt
    };

    public Return Apply(ReturnExpired @event) => this with
    {
        Status = ReturnStatus.Expired,
        ExpiredAt = @event.ExpiredAt
    };

    // ---------------------------------------------------------------------------
    // Exchange-specific Apply methods
    // ---------------------------------------------------------------------------

    public Return Apply(ExchangeApproved @event) => this with
    {
        Status = ReturnStatus.Approved,
        PriceDifference = @event.PriceDifference,
        ShipByDeadline = @event.ShipByDeadline,
        ApprovedAt = @event.ApprovedAt
    };

    public Return Apply(ExchangeDenied @event) => this with
    {
        Status = ReturnStatus.Denied,
        DenialReason = @event.Reason,
        DenialMessage = @event.Message,
        DeniedAt = @event.DeniedAt
    };

    public Return Apply(ExchangeReplacementShipped @event) => this with
    {
        Status = ReturnStatus.ExchangeShipping,
        ReplacementShipmentId = @event.ShipmentId,
        ReplacementShippedAt = @event.ShippedAt
    };

    public Return Apply(ExchangeCompleted @event) => this with
    {
        Status = ReturnStatus.Completed,
        FinalRefundAmount = @event.PriceDifferenceRefund,
        CompletedAt = @event.CompletedAt
    };

    public Return Apply(ExchangeRejected @event) => this with
    {
        Status = ReturnStatus.Rejected,
        CompletedAt = @event.RejectedAt
    };

    /// <summary>
    /// Calculates estimated refund and restocking fee based on return reasons.
    /// Defective, WrongItem, DamagedInTransit = 0% fee; Unwanted, Other = 15% fee.
    /// </summary>
    public static (decimal EstimatedRefund, decimal RestockingFee) CalculateEstimatedRefund(
        IReadOnlyList<ReturnLineItem> items)
    {
        const decimal restockingFeeRate = 0.15m;

        var totalRefund = 0m;
        var totalFee = 0m;

        foreach (var item in items)
        {
            var isFeeExempt = item.Reason is ReturnReason.Defective
                or ReturnReason.WrongItem or ReturnReason.DamagedInTransit;

            var fee = isFeeExempt ? 0m : Math.Round(item.LineTotal * restockingFeeRate, 2);
            totalFee += fee;
            totalRefund += item.LineTotal - fee;
        }

        return (totalRefund, totalFee);
    }
}
