namespace Returns.Returns;

/// <summary>
/// Event-sourced aggregate representing a customer return request.
/// Lifecycle: Requested → Approved/Denied → Received → Inspecting → Completed/Rejected/Expired
/// </summary>
public sealed record Return(
    Guid Id,
    Guid OrderId,
    Guid CustomerId,
    IReadOnlyList<ReturnLineItem> Items,
    ReturnStatus Status,
    decimal EstimatedRefundAmount,
    decimal RestockingFeeAmount,
    DateTimeOffset? ShipByDeadline,
    string? DenialReason,
    string? DenialMessage,
    string? InspectorId,
    IReadOnlyList<InspectionLineResult>? InspectionResults,
    decimal? FinalRefundAmount,
    DateTimeOffset RequestedAt,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? DeniedAt,
    DateTimeOffset? ReceivedAt,
    DateTimeOffset? InspectionStartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? ExpiredAt)
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
            EstimatedRefundAmount: estimatedRefund,
            RestockingFeeAmount: restockingFee,
            ShipByDeadline: null,
            DenialReason: null,
            DenialMessage: null,
            InspectorId: null,
            InspectionResults: null,
            FinalRefundAmount: null,
            RequestedAt: @event.RequestedAt,
            ApprovedAt: null,
            DeniedAt: null,
            ReceivedAt: null,
            InspectionStartedAt: null,
            CompletedAt: null,
            ExpiredAt: null);
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

    public Return Apply(InspectionPassed @event) => this with
    {
        Status = ReturnStatus.Completed,
        InspectionResults = @event.Results,
        FinalRefundAmount = @event.FinalRefundAmount,
        RestockingFeeAmount = @event.RestockingFeeAmount,
        CompletedAt = @event.CompletedAt
    };

    public Return Apply(InspectionFailed @event) => this with
    {
        Status = ReturnStatus.Rejected,
        InspectionResults = @event.Results,
        CompletedAt = @event.CompletedAt
    };

    public Return Apply(ReturnExpired @event) => this with
    {
        Status = ReturnStatus.Expired,
        ExpiredAt = @event.ExpiredAt
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
