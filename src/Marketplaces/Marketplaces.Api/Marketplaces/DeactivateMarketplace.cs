using Marten;
using Marketplaces.Marketplaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine.Http;

namespace Marketplaces.Api.Marketplaces;

// ---------------------------------------------------------------------------
// Handler
// ---------------------------------------------------------------------------

public static class DeactivateMarketplaceEndpoint
{
    /// <summary>
    /// Deactivates an active marketplace. Idempotent — deactivating an already
    /// deactivated marketplace returns 200 with the current state.
    /// </summary>
    [WolverinePost("/api/marketplaces/{channelCode}/deactivate")]
    [Authorize]
    public static async Task<IResult> Handle(
        string channelCode,
        IDocumentSession session,
        CancellationToken ct)
    {
        var marketplace = await session.LoadAsync<Marketplace>(channelCode, ct);
        if (marketplace is null)
            return Results.NotFound(new ProblemDetails { Detail = $"Marketplace '{channelCode}' not found.", Status = 404 });

        marketplace.IsActive = false;
        marketplace.UpdatedAt = DateTimeOffset.UtcNow;

        session.Store(marketplace);
        await session.SaveChangesAsync(ct);

        return Results.Ok(marketplace);
    }
}
