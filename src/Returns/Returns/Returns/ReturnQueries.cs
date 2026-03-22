using Marten;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Wolverine.Http;

namespace Returns.Returns;

public sealed record ReturnSummaryResponse(
    Guid ReturnId,
    Guid OrderId,
    Guid CustomerId,
    string Status,
    string StatusText,
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
    [Authorize(Policy = "CustomerService")]
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
        StatusText: EnumTranslations.ToCustomerFacingText(r.Status, r.ShipByDeadline),
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
    [Authorize(Policy = "CustomerService")]
    public static async Task<IReadOnlyList<ReturnSummaryResponse>> Handle(
        Guid? orderId,
        string? status,
        IQuerySession session,
        CancellationToken ct)
    {
        // Query inline snapshots — Marten persists the full Return aggregate
        // as a document after every event append via Snapshot<Return>(Inline)
        var queryable = session.Query<Return>().AsQueryable();

        // Filter by orderId if provided
        if (orderId.HasValue)
        {
            queryable = queryable.Where(r => r.OrderId == orderId.Value);
        }

        // Filter by status if provided
        if (!string.IsNullOrWhiteSpace(status))
        {
            // Parse status string to ReturnStatus enum
            if (Enum.TryParse<ReturnStatus>(status, ignoreCase: true, out var parsedStatus))
            {
                queryable = queryable.Where(r => r.Status == parsedStatus);
            }
        }

        var returns = await queryable
            .OrderByDescending(r => r.RequestedAt)
            .Take(100) // Limit to 100 results for performance
            .ToListAsync(ct);

        return returns
            .Select(GetReturnHandler.ToResponse)
            .ToList()
            .AsReadOnly();
    }
}
