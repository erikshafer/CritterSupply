using Marten;
using Microsoft.AspNetCore.Mvc;
using ProductCatalog.Products;
using Wolverine;
using Wolverine.Http;

namespace ProductCatalog.Api.Products;

public sealed record GetProduct(string Sku);

public static class GetProductHandler
{
    public static ProblemDetails Before(GetProduct query, Product? product)
    {
        if (product is null)
            return new ProblemDetails { Detail = "Product not found", Status = 404 };

        return WolverineContinue.NoProblems;
    }

    [WolverineGet("/api/products/{sku}")]
    public static Product Handle(GetProduct query, Product product)
    {
        return product;
    }
}
