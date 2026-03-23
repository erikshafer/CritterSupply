using Marten;
using Microsoft.AspNetCore.Authorization;
using Wolverine.Http;
using Returns.ReturnProcessing;

namespace Returns.Api.Queries;

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
