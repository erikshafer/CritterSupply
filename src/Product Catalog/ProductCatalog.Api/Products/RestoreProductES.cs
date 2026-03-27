using Marten;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProductCatalog.Products;
using Wolverine.Http;

namespace ProductCatalog.Api.Products;

public static class RestoreProductESHandler
{
    [WolverinePost("/api/products/{sku}/restore")]
    [Authorize(Policy = "ProductManager")]
    public static async Task<IResult> Handle(
        string sku,
        IDocumentSession session,
        CancellationToken ct)
    {
        var view = await session.Query<ProductCatalogView>()
            .Where(p => p.Sku == sku && p.IsDeleted)
            .FirstOrDefaultAsync(ct);

        if (view is null)
            return Results.NotFound(new ProblemDetails { Detail = "Deleted product not found", Status = 404 });

        var @event = new ProductRestored(
            ProductId: view.Id,
            RestoredAt: DateTimeOffset.UtcNow);

        session.Events.Append(view.Id, @event);
        await session.SaveChangesAsync(ct);

        return Results.NoContent();
    }
}
