using FluentValidation;
using Marten;
using Microsoft.AspNetCore.Mvc;
using ProductCatalog.Products;
using Wolverine.Http;

namespace ProductCatalog.Api.Products;

public sealed record UpdateProductTags(string Sku, List<string> NewTags);

public sealed class UpdateProductTagsValidator : AbstractValidator<UpdateProductTags>
{
    public UpdateProductTagsValidator()
    {
        RuleFor(x => x.Sku).NotEmpty();
        RuleFor(x => x.NewTags).NotNull();
        RuleForEach(x => x.NewTags).NotEmpty().MaximumLength(50);
    }
}

public static class UpdateProductTagsHandler
{
    [WolverinePut("/api/products/{sku}/tags")]
    public static async Task<IResult> Handle(
        UpdateProductTags command,
        IDocumentSession session,
        CancellationToken ct)
    {
        var view = await session.Query<ProductCatalogView>()
            .Where(p => p.Sku == command.Sku && !p.IsDeleted)
            .FirstOrDefaultAsync(ct);

        if (view is null)
            return Results.NotFound(new ProblemDetails { Detail = "Product not found", Status = 404 });

        var @event = new ProductTagsUpdated(
            ProductId: view.Id,
            PreviousTags: view.Tags,
            NewTags: command.NewTags.AsReadOnly(),
            ChangedAt: DateTimeOffset.UtcNow);

        session.Events.Append(view.Id, @event);
        await session.SaveChangesAsync(ct);

        return Results.NoContent();
    }
}
