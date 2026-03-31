using Backoffice.Clients;
using Microsoft.AspNetCore.Authorization;
using Wolverine.Http;

namespace Backoffice.Api.Queries;

/// <summary>
/// Query to list returns with optional status filter.
/// CS workflow: Return Management page — filter active return queue.
/// </summary>
public static class GetReturnsQuery
{
    [WolverineGet("/api/backoffice/returns")]
    [Authorize(Policy = "CustomerService")]
    public static async Task<IResult> Handle(
        string? status,
        IReturnsClient returnsClient,
        CancellationToken ct)
    {
        // Delegate to Returns BC via client interface
        // Note: Returns BC status values are "Requested", "Approved", "Denied", etc.
        // NOT "Pending" — that was a UI vocabulary mismatch fixed in Session 7
        var returns = await returnsClient.GetReturnsAsync(
            orderId: null,
            status: status,
            limit: null,
            ct: ct);

        return Results.Ok(returns);
    }
}
