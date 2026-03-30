using Marten;
using Marketplaces.CategoryMappings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine.Http;

namespace Marketplaces.Api.CategoryMappings;

// ---------------------------------------------------------------------------
// Handler
// ---------------------------------------------------------------------------

public static class ListCategoryMappingsEndpoint
{
    /// <summary>
    /// Lists category mappings with an optional channel code filter.
    /// </summary>
    [WolverineGet("/api/category-mappings")]
    [Authorize]
    public static async Task<IResult> Handle(
        [FromQuery] string? channelCode,
        IDocumentSession session,
        CancellationToken ct)
    {
        var query = session.Query<CategoryMapping>();

        var mappings = channelCode is not null
            ? await query.Where(m => m.ChannelCode == channelCode)
                .OrderBy(m => m.InternalCategory)
                .ToListAsync(ct)
            : await query.OrderBy(m => m.Id).ToListAsync(ct);

        return Results.Ok(mappings);
    }
}
