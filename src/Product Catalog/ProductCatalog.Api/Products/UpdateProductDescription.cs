using FluentValidation;
using Marten;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProductCatalog.Products;
using Wolverine;
using Wolverine.Http;

namespace ProductCatalog.Api.Products;

public sealed record UpdateProductDescription(
    string Sku,
    string Description)
{
    public class UpdateProductDescriptionValidator : AbstractValidator<UpdateProductDescription>
    {
        public UpdateProductDescriptionValidator()
        {
            RuleFor(x => x.Sku)
                .NotEmpty()
                .MaximumLength(24)
                .Matches(@"^[A-Z0-9\-]+$")
                .WithMessage("SKU must contain only uppercase letters (A-Z), numbers (0-9), and hyphens (-)");

            RuleFor(x => x.Description)
                .NotEmpty()
                .MaximumLength(2000);
        }
    }
}

public static class UpdateProductDescriptionHandler
{
    public static Task<Product?> Load(string sku, IDocumentSession session, CancellationToken ct)
    {
        return session.LoadAsync<Product>(sku, ct);
    }

    public static ProblemDetails Before(UpdateProductDescription command, Product? product)
    {
        if (product is null)
            return new ProblemDetails { Detail = "Product not found", Status = 404 };

        if (product.IsTerminal)
            return new ProblemDetails
            {
                Detail = "Cannot update a discontinued or deleted product",
                Status = 400
            };

        return WolverineContinue.NoProblems;
    }

    [WolverinePut("/api/products/{sku}/description")]
    [Authorize(Policy = "CopyWriter")]
    public static async Task Handle(
        UpdateProductDescription command,
        Product product,
        IDocumentSession session,
        CancellationToken ct)
    {
        var updated = product with
        {
            Description = command.Description,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        session.Store(updated);
        await session.SaveChangesAsync(ct);
    }
}
