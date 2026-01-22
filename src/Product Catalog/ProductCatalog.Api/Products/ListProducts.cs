using Marten;
using Marten.Pagination;
using ProductCatalog.Products;
using Wolverine.Http;

namespace ProductCatalog.Api.Products;

public sealed record ListProducts(
    int Page = 1,
    int PageSize = 20,
    string? Category = null,
    ProductStatus? Status = null);

public sealed record ProductListResult(
    IReadOnlyList<Product> Products,
    int Page,
    int PageSize,
    int TotalCount);

public static class ListProductsHandler
{
    [WolverineGet("/api/products")]
    public static async Task<ProductListResult> Handle(
        ListProducts query,
        IDocumentSession session,
        CancellationToken ct)
    {
        var queryable = session.Query<Product>()
            .Where(p => !p.IsDeleted);

        // Filter by category if provided
        if (query.Category is not null)
        {
            var category = CategoryName.From(query.Category);
            queryable = queryable.Where(p => p.Category == category);
        }

        // Filter by status if provided
        if (query.Status is not null)
        {
            queryable = queryable.Where(p => p.Status == query.Status.Value);
        }

        // Paginate
        var pagedList = await queryable
            .OrderBy(p => p.AddedAt)
            .ToPagedListAsync(query.Page, query.PageSize, ct);

        return new ProductListResult(
            pagedList.ToList().AsReadOnly(),
            query.Page,
            query.PageSize,
            (int)pagedList.TotalItemCount);
    }
}
