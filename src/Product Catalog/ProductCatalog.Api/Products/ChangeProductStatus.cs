using FluentValidation;
using Marten;
using Microsoft.AspNetCore.Mvc;
using ProductCatalog.Products;
using Wolverine;
using Wolverine.Http;

namespace ProductCatalog.Api.Products;

public sealed record ChangeProductStatus(string Sku, ProductStatus NewStatus)
{
    public class ChangeProductStatusValidator : AbstractValidator<ChangeProductStatus>
    {
        public ChangeProductStatusValidator()
        {
            RuleFor(x => x.Sku).NotEmpty().MaximumLength(24);
            RuleFor(x => x.NewStatus).IsInEnum();
        }
    }
}

public static class ChangeProductStatusHandler
{
    public static Task<Product?> Load(string sku, IDocumentSession session, CancellationToken ct)
    {
        return session.LoadAsync<Product>(sku, ct);
    }

    public static ProblemDetails Before(ChangeProductStatus command, Product? product)
    {
        if (product is null)
            return new ProblemDetails { Detail = "Product not found", Status = 404 };

        if (product.IsDeleted)
            return new ProblemDetails
            {
                Detail = "Cannot change status of a deleted product",
                Status = 400
            };

        return WolverineContinue.NoProblems;
    }

    [WolverinePatch("/api/products/{sku}/status")]
    public static async Task Handle(
        ChangeProductStatus command,
        Product product,
        IDocumentSession session,
        CancellationToken ct)
    {
        var updated = product.ChangeStatus(command.NewStatus);
        session.Store(updated);
        await session.SaveChangesAsync(ct);
    }
}
