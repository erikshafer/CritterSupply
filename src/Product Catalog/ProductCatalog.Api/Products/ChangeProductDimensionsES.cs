using FluentValidation;
using Marten;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProductCatalog.Products;
using Wolverine.Http;

namespace ProductCatalog.Api.Products;

public sealed record ChangeProductDimensions(string Sku, ProductDimensionsDto NewDimensions);

public sealed class ChangeProductDimensionsValidator : AbstractValidator<ChangeProductDimensions>
{
    public ChangeProductDimensionsValidator()
    {
        RuleFor(x => x.Sku).NotEmpty();
        RuleFor(x => x.NewDimensions).NotNull();
        RuleFor(x => x.NewDimensions.Length).GreaterThan(0).When(x => x.NewDimensions is not null);
        RuleFor(x => x.NewDimensions.Width).GreaterThan(0).When(x => x.NewDimensions is not null);
        RuleFor(x => x.NewDimensions.Height).GreaterThan(0).When(x => x.NewDimensions is not null);
        RuleFor(x => x.NewDimensions.Weight).GreaterThan(0).When(x => x.NewDimensions is not null);
    }
}

public static class ChangeProductDimensionsHandler
{
    [WolverinePut("/api/products/{sku}/dimensions")]
    [Authorize]
    public static async Task<IResult> Handle(
        ChangeProductDimensions command,
        IDocumentSession session,
        CancellationToken ct)
    {
        var view = await session.Query<ProductCatalogView>()
            .Where(p => p.Sku == command.Sku && !p.IsDeleted)
            .FirstOrDefaultAsync(ct);

        if (view is null)
            return Results.NotFound(new ProblemDetails { Detail = "Product not found", Status = 404 });

        var newDimensions = ProductDimensions.Create(
            command.NewDimensions.Length,
            command.NewDimensions.Width,
            command.NewDimensions.Height,
            command.NewDimensions.Weight);

        var @event = new ProductDimensionsChanged(
            ProductId: view.Id,
            PreviousDimensions: view.Dimensions,
            NewDimensions: newDimensions,
            ChangedAt: DateTimeOffset.UtcNow);

        session.Events.Append(view.Id, @event);
        await session.SaveChangesAsync(ct);

        return Results.NoContent();
    }
}
