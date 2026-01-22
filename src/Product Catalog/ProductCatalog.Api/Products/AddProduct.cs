using FluentValidation;
using Marten;
using ProductCatalog.Products;
using Wolverine.Http;

namespace ProductCatalog.Api.Products;

public sealed record AddProduct(
    string Sku,
    string Name,
    string Description,
    string Category,
    string? LongDescription = null,
    string? Subcategory = null,
    string? Brand = null,
    List<ProductImageDto>? Images = null,
    List<string>? Tags = null,
    ProductDimensionsDto? Dimensions = null)
{
    public class AddProductValidator : AbstractValidator<AddProduct>
    {
        public AddProductValidator()
        {
            RuleFor(x => x.Sku).NotEmpty().MaximumLength(24);
            RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
            RuleFor(x => x.Description).NotEmpty().MaximumLength(2000);
            RuleFor(x => x.Category).NotEmpty().MaximumLength(50);
            RuleFor(x => x.LongDescription).MaximumLength(5000).When(x => x.LongDescription is not null);
            RuleFor(x => x.Subcategory).MaximumLength(50).When(x => x.Subcategory is not null);
            RuleFor(x => x.Brand).MaximumLength(100).When(x => x.Brand is not null);
        }
    }
}

public sealed record ProductImageDto(string Url, string AltText, int SortOrder = 0);

public sealed record ProductDimensionsDto(decimal Length, decimal Width, decimal Height, decimal Weight);

public static class AddProductHandler
{
    [WolverinePost("/api/products")]
    public static async Task<CreationResponse> Handle(
        AddProduct command,
        IDocumentSession session,
        CancellationToken ct)
    {
        // Convert DTOs to value objects
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

        var product = Product.Create(
            command.Sku,
            command.Name,
            command.Description,
            command.Category,
            images,
            command.LongDescription,
            command.Subcategory,
            command.Brand,
            tags,
            dimensions);

        session.Store(product);
        await session.SaveChangesAsync(ct);

        return new CreationResponse($"/api/products/{command.Sku}");
    }
}
