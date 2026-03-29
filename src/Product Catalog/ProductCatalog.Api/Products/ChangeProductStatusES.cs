using FluentValidation;
using Marten;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProductCatalog.Products;
using Wolverine.Http;

namespace ProductCatalog.Api.Products;

public sealed record ChangeProductStatusCommand(string Sku, ProductStatus NewStatus, string? Reason = null);

public sealed class ChangeProductStatusCommandValidator : AbstractValidator<ChangeProductStatusCommand>
{
    public ChangeProductStatusCommandValidator()
    {
        RuleFor(x => x.Sku).NotEmpty();
        RuleFor(x => x.NewStatus).IsInEnum();
    }
}

public static class ChangeProductStatusESHandler
{
    [WolverinePatch("/api/products/{sku}/status")]
    [Authorize]
    public static async Task<IResult> Handle(
        ChangeProductStatusCommand command,
        IDocumentSession session,
        CancellationToken ct)
    {
        var view = await session.Query<ProductCatalogView>()
            .Where(p => p.Sku == command.Sku)
            .FirstOrDefaultAsync(ct);

        if (view is null)
            return Results.NotFound(new ProblemDetails { Detail = "Product not found", Status = 404 });

        if (view.IsDeleted)
            return Results.Problem(detail: "Cannot change status of a deleted product", statusCode: 400);

        var @event = new ProductStatusChanged(
            ProductId: view.Id,
            PreviousStatus: view.Status,
            NewStatus: command.NewStatus,
            Reason: command.Reason,
            ChangedAt: DateTimeOffset.UtcNow);

        session.Events.Append(view.Id, @event);
        await session.SaveChangesAsync(ct);

        return Results.NoContent();
    }
}
