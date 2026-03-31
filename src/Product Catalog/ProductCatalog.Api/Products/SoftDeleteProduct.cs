using Marten;
using Messages.Contracts.ProductCatalog;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProductCatalog.Products;
using Wolverine;
using Wolverine.Http;

namespace ProductCatalog.Api.Products;

public static class SoftDeleteProductHandler
{
    [WolverineDelete("/api/products/{sku}")]
    [Authorize(Policy = "ProductManager")]
    public static async Task<(IResult, OutgoingMessages)> Handle(
        string sku,
        IDocumentSession session,
        CancellationToken ct)
    {
        var outgoing = new OutgoingMessages();

        var view = await session.Query<ProductCatalogView>()
            .Where(p => p.Sku == sku && !p.IsDeleted)
            .FirstOrDefaultAsync(ct);

        if (view is null)
            return (Results.NotFound(new ProblemDetails { Detail = "Product not found", Status = 404 }), outgoing);

        var @event = new ProductSoftDeleted(
            ProductId: view.Id,
            DeletedAt: DateTimeOffset.UtcNow);

        session.Events.Append(view.Id, @event);

        outgoing.Add(new ProductDeleted(
            Sku: sku,
            OccurredAt: @event.DeletedAt));

        return (Results.NoContent(), outgoing);
    }
}
