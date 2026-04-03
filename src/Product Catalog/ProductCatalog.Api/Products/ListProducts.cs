using Marten;
using ProductCatalog.Products;
using Wolverine.Http;

namespace ProductCatalog.Api.Products;

public static class ListProductsHandler
{
    [WolverineGet("/api/products")]
    public static async Task<IResult> Handle(
        int? page,
        int? pageSize,
        string? category,
        string? status,
        IDocumentSession session,
        CancellationToken ct)
    {
        var query = session.Query<ProductCatalogView>()
            .Where(p => !p.IsDeleted);

        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(p => p.Category == category);

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<ProductStatus>(status, true, out var statusValue))
            query = query.Where(p => p.Status == statusValue);

        var currentPage = Math.Max(1, page ?? 1);
        var size = Math.Clamp(pageSize ?? 20, 1, 100);

        var totalCount = await query.CountAsync(ct);
        var products = await query
            .OrderBy(p => p.Sku)
            .Skip((currentPage - 1) * size)
            .Take(size)
            .ToListAsync(ct);

        return Results.Ok(new
        {
            Items = products,
            TotalCount = totalCount,
            Page = currentPage,
            PageSize = size
        });
    }
}
