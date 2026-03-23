using Marten;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Wolverine.Http;
using Returns.Returns;

namespace Returns.Api.Queries;

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
