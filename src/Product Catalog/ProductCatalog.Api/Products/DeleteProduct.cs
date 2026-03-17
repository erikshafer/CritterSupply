using FluentValidation;
using Marten;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProductCatalog.Products;
using Wolverine;
using Wolverine.Http;

namespace ProductCatalog.Api.Products;

public sealed record DeleteProduct(string Sku)
{
    public class DeleteProductValidator : AbstractValidator<DeleteProduct>
    {
        public DeleteProductValidator()
        {
            RuleFor(x => x.Sku)
                .NotEmpty()
                .MaximumLength(24)
                .Matches(@"^[A-Z0-9\-]+$")
                .WithMessage("SKU must contain only uppercase letters (A-Z), numbers (0-9), and hyphens (-)");
        }
    }
}

public static class DeleteProductHandler
{
    public static Task<Product?> Load(string sku, IDocumentSession session, CancellationToken ct)
    {
        return session.LoadAsync<Product>(sku, ct);
    }

    public static ProblemDetails Before(DeleteProduct command, Product? product)
    {
        if (product is null)
            return new ProblemDetails { Detail = "Product not found", Status = 404 };

        if (product.IsDeleted)
            return new ProblemDetails
            {
                Detail = "Product is already deleted",
                Status = 400
            };

        return WolverineContinue.NoProblems;
    }

    [WolverineDelete("/api/products/{sku}")]
    [Authorize(Policy = "ProductManager")]
    public static async Task Handle(
        DeleteProduct command,
        Product product,
        IDocumentSession session,
        CancellationToken ct)
    {
        var deleted = product.SoftDelete();

        session.Store(deleted);
        await session.SaveChangesAsync(ct);
    }
}
