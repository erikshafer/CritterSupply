using Backoffice.Clients;
using Microsoft.AspNetCore.Authorization;
using Wolverine.Http;

namespace Backoffice.Api.Queries;

public sealed record GetProductList(
    int? Page,
    int? PageSize,
    string? Category,
    string? Status);

public static class GetProductListHandler
{
    [WolverineGet("/api/catalog/products")]
    [Authorize(Policy = "ProductManager")]
    public static async Task<ProductListResult?> Handle(
        GetProductList query,
        ICatalogClient catalogClient,
        CancellationToken ct)
    {
        return await catalogClient.ListProductsAsync(
            query.Page ?? 1,
            query.PageSize ?? 25,
            query.Category,
            query.Status,
            ct);
    }
}
