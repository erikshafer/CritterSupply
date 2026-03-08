using Marten;
using Microsoft.AspNetCore.Mvc;
using Pricing.Products;
using Wolverine.Http;

namespace Pricing.Api.Pricing;

public static class GetBulkPrices
{
    /// <summary>
    /// GET /api/pricing/products?skus={comma-separated}
    /// Retrieves current prices for multiple SKUs in a single query.
    /// Uses Marten's LoadManyAsync for efficient bulk loading (single query with WHERE id = ANY(@ids)).
    /// Returns partial results if some SKUs are missing (no 404 for partial matches).
    /// Limit: 50 SKUs per request.
    /// SLA: < 100ms p95 for 50 SKUs.
    /// </summary>
    [WolverineGet("/api/pricing/products")]
    public static async Task<IResult> Handle(
        [FromQuery] string skus,
        IDocumentSession session)
    {
        if (string.IsNullOrWhiteSpace(skus))
        {
            return Results.BadRequest(new { message = "Query parameter 'skus' is required." });
        }

        // Parse and normalize SKUs to uppercase
        var skuList = skus.Split(',')
            .Select(s => s.Trim().ToUpperInvariant())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct()
            .ToList();

        if (skuList.Count == 0)
        {
            return Results.BadRequest(new { message = "No valid SKUs provided." });
        }

        if (skuList.Count > 50)
        {
            return Results.BadRequest(new { message = "Maximum 50 SKUs per request. Received: " + skuList.Count });
        }

        // Marten's LoadManyAsync: single query with WHERE id = ANY(@ids)
        var prices = await session.LoadManyAsync<CurrentPriceView>(skuList);

        return Results.Ok(prices);
    }
}
