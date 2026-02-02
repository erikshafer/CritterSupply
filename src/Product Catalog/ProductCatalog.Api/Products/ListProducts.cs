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
        IDocumentSession session,
        int? page,
        int? pageSize,
        string? category,
        ProductStatus? status,
        CancellationToken ct)
    {
        // Apply defaults for pagination
        var actualPage = page ?? 1;
        var actualPageSize = pageSize ?? 20;

        var queryable = session.Query<Product>()
            .Where(p => !p.IsDeleted);

        // Filter by category if provided
        if (category is not null)
        {
            queryable = queryable.Where(p => p.Category == category);
        }

        // Filter by status if provided
        if (status is not null)
        {
            queryable = queryable.Where(p => p.Status == status.Value);
        }

        // Paginate
        var pagedList = await queryable
            .OrderBy(p => p.AddedAt)
            .ToPagedListAsync(actualPage, actualPageSize, ct);

        return new ProductListResult(
            pagedList.ToList().AsReadOnly(),
            actualPage,
            actualPageSize,
            (int)pagedList.TotalItemCount);
    }
}
