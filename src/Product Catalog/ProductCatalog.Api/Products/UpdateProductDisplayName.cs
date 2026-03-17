using FluentValidation;
using Marten;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProductCatalog.Products;
using Wolverine;
using Wolverine.Http;

namespace ProductCatalog.Api.Products;

public sealed record UpdateProductDisplayName(
    string Sku,
    string Name)
{
    public class UpdateProductDisplayNameValidator : AbstractValidator<UpdateProductDisplayName>
    {
        public UpdateProductDisplayNameValidator()
        {
            RuleFor(x => x.Sku)
                .NotEmpty()
                .MaximumLength(24)
                .Matches(@"^[A-Z0-9\-]+$")
                .WithMessage("SKU must contain only uppercase letters (A-Z), numbers (0-9), and hyphens (-)");

            RuleFor(x => x.Name)
                .NotEmpty()
                .MaximumLength(100)
                .Matches(@"^[A-Za-z0-9\s.,!&()\-]+$")
                .WithMessage("Product name contains invalid characters. Allowed: letters, numbers, spaces, and . , ! & ( ) -");
        }
    }
}

public static class UpdateProductDisplayNameHandler
{
    public static Task<Product?> Load(string sku, IDocumentSession session, CancellationToken ct)
    {
        return session.LoadAsync<Product>(sku, ct);
    }

    public static ProblemDetails Before(UpdateProductDisplayName command, Product? product)
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

    [WolverinePut("/api/products/{sku}/display-name")]
    [Authorize(Policy = "ProductManager")]
    public static async Task Handle(
        UpdateProductDisplayName command,
        Product product,
        IDocumentSession session,
        CancellationToken ct)
    {
        var updated = product with
        {
            Name = ProductName.From(command.Name),
            UpdatedAt = DateTimeOffset.UtcNow
        };

        session.Store(updated);
        await session.SaveChangesAsync(ct);
    }
}
