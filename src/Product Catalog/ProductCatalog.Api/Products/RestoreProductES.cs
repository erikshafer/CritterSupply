using Marten;
using Messages.Contracts.ProductCatalog;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProductCatalog.Products;
using Wolverine;
using Wolverine.Http;
using IntegrationProductRestored = Messages.Contracts.ProductCatalog.ProductRestored;

namespace ProductCatalog.Api.Products;

public static class RestoreProductESHandler
{
    [WolverinePost("/api/products/{sku}/restore")]
    [Authorize(Policy = "ProductManager")]
    public static async Task<(IResult, OutgoingMessages)> Handle(
        string sku,
        IDocumentSession session,
        CancellationToken ct)
    {
        var outgoing = new OutgoingMessages();

        var view = await session.Query<ProductCatalogView>()
            .Where(p => p.Sku == sku && p.IsDeleted)
            .FirstOrDefaultAsync(ct);

        if (view is null)
            return (Results.NotFound(new ProblemDetails { Detail = "Deleted product not found", Status = 404 }), outgoing);

        var @event = new ProductCatalog.Products.ProductRestored(
            ProductId: view.Id,
            RestoredAt: DateTimeOffset.UtcNow);

        session.Events.Append(view.Id, @event);

        outgoing.Add(new IntegrationProductRestored(
            Sku: sku,
            OccurredAt: @event.RestoredAt));

        return (Results.NoContent(), outgoing);
    }
}
