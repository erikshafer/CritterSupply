using FluentValidation;
using Marten;
using Microsoft.AspNetCore.Authorization;
using ProductCatalog.Products;
using Wolverine.Http;

namespace ProductCatalog.Api.Products;

public sealed record CreateProduct(
    string Sku,
    string Name,
    string Description,
    string Category,
    string? LongDescription = null,
    string? Subcategory = null,
    string? Brand = null,
    List<ProductImageDto>? Images = null,
    List<string>? Tags = null,
    ProductDimensionsDto? Dimensions = null);

public sealed class CreateProductValidator : AbstractValidator<CreateProduct>
{
    public CreateProductValidator()
    {
        RuleFor(x => x.Sku).NotEmpty().MaximumLength(24)
            .Matches(@"^[A-Z0-9\-]+$")
            .WithMessage("SKU must contain only uppercase letters (A-Z), numbers (0-9), and hyphens (-)");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100)
            .Matches(@"^[A-Za-z0-9\s.,!&()\-]+$")
            .WithMessage("Product name contains invalid characters.");
        RuleFor(x => x.Description).NotEmpty().MaximumLength(2000);
        RuleFor(x => x.Category).NotEmpty().MaximumLength(50);
        RuleFor(x => x.LongDescription).MaximumLength(5000).When(x => x.LongDescription is not null);
        RuleFor(x => x.Subcategory).MaximumLength(50).When(x => x.Subcategory is not null);
        RuleFor(x => x.Brand).MaximumLength(100).When(x => x.Brand is not null);
    }
}

public static class CreateProductHandler
{
    [WolverinePost("/api/products")]
    [Authorize(Policy = "VendorAdmin")]
    public static async Task<IResult> Handle(
        CreateProduct command,
        IDocumentSession session,
        CancellationToken ct)
    {
        // Check for duplicate SKU via projection
        var existing = await session.Query<ProductCatalogView>()
            .Where(p => p.Sku == command.Sku)
            .AnyAsync(ct);

        if (existing)
            return Results.Conflict(new { Message = "Product with SKU already exists" });

        var productId = Guid.NewGuid();
        var images = command.Images?
            .Select(dto => ProductImage.Create(dto.Url, dto.AltText, dto.SortOrder))
            .ToList()
            .AsReadOnly() as IReadOnlyList<ProductImage>;

        var dimensions = command.Dimensions is not null
            ? ProductDimensions.Create(command.Dimensions.Length, command.Dimensions.Width,
                command.Dimensions.Height, command.Dimensions.Weight)
            : null;

        var @event = new ProductCreated(
            ProductId: productId,
            Sku: command.Sku,
            Name: command.Name,
            Description: command.Description,
            Category: command.Category,
            LongDescription: command.LongDescription,
            Subcategory: command.Subcategory,
            Brand: command.Brand,
            Images: images,
            Tags: command.Tags?.AsReadOnly(),
            Dimensions: dimensions,
            CreatedAt: DateTimeOffset.UtcNow);

        session.Events.StartStream<CatalogProduct>(productId, @event);
        await session.SaveChangesAsync(ct);

        return Results.Created($"/api/products/{command.Sku}", new { Sku = command.Sku, ProductId = productId });
    }
}
