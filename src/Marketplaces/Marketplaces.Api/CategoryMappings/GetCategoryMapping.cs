using Marten;
using Marketplaces.CategoryMappings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine.Http;

namespace Marketplaces.Api.CategoryMappings;

// ---------------------------------------------------------------------------
// Handler
// ---------------------------------------------------------------------------

public static class GetCategoryMappingEndpoint
{
    /// <summary>
    /// Gets a category mapping by channel code and internal category.
    /// Builds the composite key "{channelCode}:{internalCategory}" internally.
    /// </summary>
    [WolverineGet("/api/category-mappings/{channelCode}/{internalCategory}")]
    [Authorize]
    public static async Task<IResult> Handle(
        string channelCode,
        string internalCategory,
        IDocumentSession session,
        CancellationToken ct)
    {
        var compositeId = $"{channelCode}:{internalCategory}";
        var mapping = await session.LoadAsync<CategoryMapping>(compositeId, ct);

        if (mapping is null)
            return Results.NotFound(new ProblemDetails
            {
                Detail = $"Category mapping '{compositeId}' not found.",
                Status = 404
            });

        return Results.Ok(mapping);
    }
}
