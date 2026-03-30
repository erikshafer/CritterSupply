using Marten;
using Marketplaces.Marketplaces;
using Microsoft.AspNetCore.Authorization;
using Wolverine.Http;

namespace Marketplaces.Api.Marketplaces;

// ---------------------------------------------------------------------------
// Handler
// ---------------------------------------------------------------------------

public static class ListMarketplacesEndpoint
{
    [WolverineGet("/api/marketplaces")]
    [Authorize]
    public static async Task<IResult> Handle(
        IDocumentSession session,
        CancellationToken ct)
    {
        var marketplaces = await session.Query<Marketplace>()
            .OrderBy(m => m.Id)
            .ToListAsync(ct);

        return Results.Ok(marketplaces);
    }
}
