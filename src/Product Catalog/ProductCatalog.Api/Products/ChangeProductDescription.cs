using FluentValidation;
using Marten;
using Messages.Contracts.ProductCatalog;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProductCatalog.Products;
using Wolverine;
using Wolverine.Http;

namespace ProductCatalog.Api.Products;

public sealed record ChangeProductDescription(string Sku, string NewDescription);

public sealed class ChangeProductDescriptionValidator : AbstractValidator<ChangeProductDescription>
{
    public ChangeProductDescriptionValidator()
    {
        RuleFor(x => x.Sku).NotEmpty();
        RuleFor(x => x.NewDescription).NotEmpty().MaximumLength(2000);
    }
}

public static class ChangeProductDescriptionHandler
{
    [WolverinePut("/api/products/{sku}/description")]
    [Authorize]
    public static async Task<(IResult, OutgoingMessages)> Handle(
        ChangeProductDescription command,
        IDocumentSession session,
        CancellationToken ct)
    {
        var outgoing = new OutgoingMessages();

        var view = await session.Query<ProductCatalogView>()
            .Where(p => p.Sku == command.Sku && !p.IsDeleted)
            .FirstOrDefaultAsync(ct);

        if (view is null)
            return (Results.NotFound(new ProblemDetails { Detail = "Product not found", Status = 404 }), outgoing);

        var @event = new ProductDescriptionChanged(
            ProductId: view.Id,
            PreviousDescription: view.Description,
            NewDescription: command.NewDescription,
            ChangedAt: DateTimeOffset.UtcNow);

        session.Events.Append(view.Id, @event);

        outgoing.Add(new ProductContentUpdated(
            Sku: command.Sku,
            Name: view.Name,
            Description: command.NewDescription,
            OccurredAt: @event.ChangedAt));

        return (Results.NoContent(), outgoing);
    }
}
