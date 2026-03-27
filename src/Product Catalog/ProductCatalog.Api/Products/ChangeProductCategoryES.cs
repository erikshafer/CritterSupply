using FluentValidation;
using Marten;
using Microsoft.AspNetCore.Mvc;
using ProductCatalog.Products;
using Wolverine.Http;

namespace ProductCatalog.Api.Products;

public sealed record ChangeProductCategory(string Sku, string NewCategory);

public sealed class ChangeProductCategoryValidator : AbstractValidator<ChangeProductCategory>
{
    public ChangeProductCategoryValidator()
    {
        RuleFor(x => x.Sku).NotEmpty();
        RuleFor(x => x.NewCategory).NotEmpty().MaximumLength(50);
    }
}

public static class ChangeProductCategoryHandler
{
    [WolverinePut("/api/products/{sku}/category")]
    public static async Task<IResult> Handle(
        ChangeProductCategory command,
        IDocumentSession session,
        CancellationToken ct)
    {
        var view = await session.Query<ProductCatalogView>()
            .Where(p => p.Sku == command.Sku && !p.IsDeleted)
            .FirstOrDefaultAsync(ct);

        if (view is null)
            return Results.NotFound(new ProblemDetails { Detail = "Product not found", Status = 404 });

        var @event = new ProductCategoryChanged(
            ProductId: view.Id,
            PreviousCategory: view.Category,
            NewCategory: command.NewCategory,
            ChangedAt: DateTimeOffset.UtcNow);

        session.Events.Append(view.Id, @event);
        await session.SaveChangesAsync(ct);

        return Results.NoContent();
    }
}
