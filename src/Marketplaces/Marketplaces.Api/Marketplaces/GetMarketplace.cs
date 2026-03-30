using Marten;
using Marketplaces.Marketplaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine.Http;

namespace Marketplaces.Api.Marketplaces;

// ---------------------------------------------------------------------------
// Handler
// ---------------------------------------------------------------------------

public static class GetMarketplaceEndpoint
{
    [WolverineGet("/api/marketplaces/{channelCode}")]
    [Authorize]
    public static async Task<IResult> Handle(
        string channelCode,
        IDocumentSession session,
        CancellationToken ct)
    {
        var marketplace = await session.LoadAsync<Marketplace>(channelCode, ct);
        if (marketplace is null)
            return Results.NotFound(new ProblemDetails { Detail = $"Marketplace '{channelCode}' not found.", Status = 404 });

        return Results.Ok(marketplace);
    }
}
