using Marten;
using Microsoft.AspNetCore.Mvc;
using ProductCatalog.Products;
using Wolverine.Http;

namespace ProductCatalog.Api.Products;

public static class GetProductESHandler
{
    [WolverineGet("/api/products/{sku}")]
    public static async Task<IResult> Handle(
        string sku,
        IDocumentSession session,
        CancellationToken ct)
    {
        var view = await session.Query<ProductCatalogView>()
            .Where(p => p.Sku == sku && !p.IsDeleted)
            .FirstOrDefaultAsync(ct);

        if (view is null)
            return Results.NotFound(new ProblemDetails { Detail = "Product not found", Status = 404 });

        return Results.Ok(view);
    }
}
