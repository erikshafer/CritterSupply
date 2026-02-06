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
            // SKU validation - must match Sku value object rules
            RuleFor(x => x.Sku)
                .NotEmpty()
                .MaximumLength(24)
                .Matches(@"^[A-Z0-9\-]+$")
                .WithMessage("SKU must contain only uppercase letters (A-Z), numbers (0-9), and hyphens (-)");

            // Product name validation - must match ProductName value object rules
            RuleFor(x => x.Name)
                .NotEmpty()
                .MaximumLength(100)
                .Matches(@"^[A-Za-z0-9\s.,!&()\-]+$")
                .WithMessage("Product name contains invalid characters. Allowed: letters, numbers, spaces, and . , ! & ( ) -");

            // Description validation
            RuleFor(x => x.Description)
                .NotEmpty()
                .MaximumLength(2000);

            // Category validation - must match CategoryName value object rules
            RuleFor(x => x.Category)
                .NotEmpty()
                .MaximumLength(50);

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
        // Check for duplicate SKU
        var existing = await session.LoadAsync<Product>(command.Sku, ct);
        if (existing is not null)
        {
            throw new InvalidOperationException("Product with SKU already exists");
        }

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
