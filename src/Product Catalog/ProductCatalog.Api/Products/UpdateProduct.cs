using FluentValidation;
using Marten;
using Microsoft.AspNetCore.Mvc;
using ProductCatalog.Products;
using Wolverine;
using Wolverine.Http;

namespace ProductCatalog.Api.Products;

public sealed record UpdateProduct(
    string Sku,
    string? Name = null,
    string? Description = null,
    string? LongDescription = null,
    string? Category = null,
    string? Subcategory = null,
    string? Brand = null,
    List<ProductImageDto>? Images = null,
    List<string>? Tags = null,
    ProductDimensionsDto? Dimensions = null)
{
    public class UpdateProductValidator : AbstractValidator<UpdateProduct>
    {
        public UpdateProductValidator()
        {
            RuleFor(x => x.Sku).NotEmpty().MaximumLength(24);
            RuleFor(x => x.Name).MaximumLength(100).When(x => x.Name is not null);
            RuleFor(x => x.Description).MaximumLength(2000).When(x => x.Description is not null);
            RuleFor(x => x.Category).MaximumLength(50).When(x => x.Category is not null);
            RuleFor(x => x.LongDescription).MaximumLength(5000).When(x => x.LongDescription is not null);
            RuleFor(x => x.Subcategory).MaximumLength(50).When(x => x.Subcategory is not null);
            RuleFor(x => x.Brand).MaximumLength(100).When(x => x.Brand is not null);
        }
    }
}

public static class UpdateProductHandler
{
    public static ProblemDetails Before(UpdateProduct command, Product? product)
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

    [WolverinePut("/api/products/{sku}")]
    public static async Task Handle(
        UpdateProduct command,
        Product product,
        IDocumentSession session,
        CancellationToken ct)
    {
        // Convert DTOs to value objects if provided
        var images = command.Images?
            .Select(dto => ProductImage.Create(dto.Url, dto.AltText, dto.SortOrder))
            .ToList()
            .AsReadOnly();

        var dimensions = command.Dimensions is not null
            ? ProductDimensions.Create(
                command.Dimensions.Length,
                command.Dimensions.Width,
                command.Dimensions.Height,
                command.Dimensions.Weight)
            : null;

        var tags = command.Tags?.ToList().AsReadOnly();

        var updated = product.Update(
            command.Name,
            command.Description,
            command.LongDescription,
            command.Category,
            command.Subcategory,
            command.Brand,
            images,
            tags,
            dimensions);

        session.Store(updated);
        await session.SaveChangesAsync(ct);
    }
}
