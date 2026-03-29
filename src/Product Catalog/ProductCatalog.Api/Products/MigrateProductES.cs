using Marten;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProductCatalog.Products;
using Wolverine.Http;

namespace ProductCatalog.Api.Products;

public sealed record MigrateProduct(string Sku);

public static class MigrateProductHandler
{
    [WolverinePost("/api/products/{sku}/migrate")]
    [Authorize]
    public static async Task<IResult> Handle(
        MigrateProduct command,
        IDocumentSession session,
        CancellationToken ct)
    {
        // Load existing document-store product
        var existing = await session.LoadAsync<Product>(command.Sku, ct);
        if (existing is null)
            return Results.NotFound(new ProblemDetails { Detail = "Product not found in document store", Status = 404 });

        // Check if already migrated (idempotent)
        var alreadyMigrated = await session.Query<ProductCatalogView>()
            .Where(p => p.Sku == command.Sku)
            .AnyAsync(ct);

        if (alreadyMigrated)
            return Results.Ok(new { Message = "Product already migrated", Sku = command.Sku });

        var productId = Guid.NewGuid();
        var @event = new ProductMigrated(
            ProductId: productId,
            Sku: (string)existing.Sku,
            Name: (string)existing.Name,
            Description: existing.Description,
            LongDescription: existing.LongDescription,
            Category: existing.Category,
            Subcategory: existing.Subcategory,
            Brand: existing.Brand,
            Images: existing.Images,
            Tags: existing.Tags,
            Dimensions: existing.Dimensions,
            Status: existing.Status,
            IsDeleted: existing.IsDeleted,
            VendorTenantId: existing.VendorTenantId,
            AssignedBy: existing.AssignedBy,
            AssignedAt: existing.AssignedAt,
            AddedAt: existing.AddedAt,
            MigratedAt: DateTimeOffset.UtcNow);

        session.Events.StartStream<CatalogProduct>(productId, @event);
        await session.SaveChangesAsync(ct);

        return Results.Ok(new { Message = "Product migrated to event sourcing", Sku = command.Sku, ProductId = productId });
    }
}
