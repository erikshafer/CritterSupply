using FluentValidation;
using Marten;
using Microsoft.AspNetCore.Mvc;
using ProductCatalog.Products;
using Wolverine.Http;

namespace ProductCatalog.Api.Products;

public sealed record UpdateProductImages(string Sku, List<ProductImageDto> NewImages);

public sealed class UpdateProductImagesValidator : AbstractValidator<UpdateProductImages>
{
    public UpdateProductImagesValidator()
    {
        RuleFor(x => x.Sku).NotEmpty();
        RuleFor(x => x.NewImages).NotNull();
        RuleForEach(x => x.NewImages).ChildRules(image =>
        {
            image.RuleFor(i => i.Url).NotEmpty();
            image.RuleFor(i => i.AltText).NotEmpty();
            image.RuleFor(i => i.SortOrder).GreaterThanOrEqualTo(0);
        });
    }
}

public static class UpdateProductImagesHandler
{
    [WolverinePut("/api/products/{sku}/images")]
    public static async Task<IResult> Handle(
        UpdateProductImages command,
        IDocumentSession session,
        CancellationToken ct)
    {
        var view = await session.Query<ProductCatalogView>()
            .Where(p => p.Sku == command.Sku && !p.IsDeleted)
            .FirstOrDefaultAsync(ct);

        if (view is null)
            return Results.NotFound(new ProblemDetails { Detail = "Product not found", Status = 404 });

        var newImages = command.NewImages
            .Select(dto => ProductImage.Create(dto.Url, dto.AltText, dto.SortOrder))
            .ToList()
            .AsReadOnly();

        var @event = new ProductImagesUpdated(
            ProductId: view.Id,
            PreviousImages: view.Images,
            NewImages: newImages,
            ChangedAt: DateTimeOffset.UtcNow);

        session.Events.Append(view.Id, @event);
        await session.SaveChangesAsync(ct);

        return Results.NoContent();
    }
}
