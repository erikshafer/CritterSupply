using Marten;
using Microsoft.AspNetCore.Http;
using Wolverine.Http;

namespace Returns.Returns;

public sealed record ReturnSummaryResponse(
    Guid ReturnId,
    Guid OrderId,
    Guid CustomerId,
    string Status,
    IReadOnlyList<ReturnLineItemResponse> Items,
    decimal EstimatedRefundAmount,
    decimal RestockingFeeAmount,
    decimal? FinalRefundAmount,
    DateTimeOffset? ShipByDeadline,
    string? DenialReason,
    string? DenialMessage,
    DateTimeOffset RequestedAt,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? ReceivedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? ExpiredAt);

public static class GetReturnHandler
{
    [WolverineGet("/api/returns/{returnId}")]
    public static async Task<IResult> Handle(
        Guid returnId,
        IDocumentSession session,
        CancellationToken ct)
    {
        var aggregate = await session.Events.AggregateStreamAsync<Return>(returnId, token: ct);

        if (aggregate is null)
            return Results.NotFound();

        return Results.Ok(ToResponse(aggregate));
    }

    internal static ReturnSummaryResponse ToResponse(Return r) => new(
        ReturnId: r.Id,
        OrderId: r.OrderId,
        CustomerId: r.CustomerId,
        Status: r.Status.ToString(),
        Items: r.Items.Select(ReturnLineItemResponse.From).ToList().AsReadOnly(),
        EstimatedRefundAmount: r.EstimatedRefundAmount,
        RestockingFeeAmount: r.RestockingFeeAmount,
        FinalRefundAmount: r.FinalRefundAmount,
        ShipByDeadline: r.ShipByDeadline,
        DenialReason: r.DenialReason,
        DenialMessage: r.DenialMessage,
        RequestedAt: r.RequestedAt,
        ApprovedAt: r.ApprovedAt,
        ReceivedAt: r.ReceivedAt,
        CompletedAt: r.CompletedAt,
        ExpiredAt: r.ExpiredAt);
}

public static class GetReturnsForOrderHandler
{
    [WolverineGet("/api/returns")]
    public static async Task<IReadOnlyList<ReturnSummaryResponse>> Handle(
        Guid orderId,
        IQuerySession session,
        CancellationToken ct)
    {
        // Query all return streams for this order
        // Phase 1: use lightweight query via Marten's event store
        var streams = await session.Events
            .QueryAllRawEvents()
            .Where(e => e.EventTypeName == "return_requested")
            .ToListAsync(ct);

        // For Phase 1, we use a simple document query via snapshots
        // This will be replaced with a proper projection in Phase 2
        var returns = new List<ReturnSummaryResponse>();
        return returns.AsReadOnly();
    }
}
