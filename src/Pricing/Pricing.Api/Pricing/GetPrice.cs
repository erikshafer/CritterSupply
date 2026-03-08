using Marten;
using Pricing.Products;
using Wolverine.Http;

namespace Pricing.Api.Pricing;

public static class GetPrice
{
    /// <summary>
    /// GET /api/pricing/products/{sku}
    /// Retrieves the current price for a single SKU.
    /// Returns 404 if the SKU has not been registered in Pricing BC yet.
    /// </summary>
    [WolverineGet("/api/pricing/products/{sku}")]
    public static async Task<IResult> Handle(
        string sku,
        IDocumentSession session)
    {
        // CRITICAL: Normalize SKU to uppercase - Marten string document IDs are case-sensitive
        var normalizedSku = sku.ToUpperInvariant();

        var priceView = await session.LoadAsync<CurrentPriceView>(normalizedSku);

        return priceView is null
            ? Results.NotFound(new { message = $"SKU '{sku}' has not been registered in Pricing yet." })
            : Results.Ok(priceView);
    }
}
