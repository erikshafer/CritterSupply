using FluentValidation;
using Marten;
using Messages.Contracts.ProductCatalog;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProductCatalog.Products;
using Wolverine;
using Wolverine.Http;

namespace ProductCatalog.Api.Products;

public sealed record ChangeProductName(string Sku, string NewName);

public sealed class ChangeProductNameValidator : AbstractValidator<ChangeProductName>
{
    public ChangeProductNameValidator()
    {
        RuleFor(x => x.Sku).NotEmpty();
        RuleFor(x => x.NewName).NotEmpty().MaximumLength(100)
            .Matches(@"^[A-Za-z0-9\s.,!&()\-]+$")
            .WithMessage("Product name contains invalid characters.");
    }
}

public static class ChangeProductNameHandler
{
    [WolverinePut("/api/products/{sku}/name")]
    [Authorize]
    public static async Task<(IResult, OutgoingMessages)> Handle(
        ChangeProductName command,
        IDocumentSession session,
        CancellationToken ct)
    {
        var outgoing = new OutgoingMessages();

        var view = await session.Query<ProductCatalogView>()
            .Where(p => p.Sku == command.Sku && !p.IsDeleted)
            .FirstOrDefaultAsync(ct);

        if (view is null)
            return (Results.NotFound(new ProblemDetails { Detail = "Product not found", Status = 404 }), outgoing);

        var @event = new ProductNameChanged(
            ProductId: view.Id,
            PreviousName: view.Name,
            NewName: command.NewName,
            ChangedAt: DateTimeOffset.UtcNow);

        session.Events.Append(view.Id, @event);

        outgoing.Add(new ProductContentUpdated(
            Sku: command.Sku,
            Name: command.NewName,
            Description: view.Description,
            OccurredAt: @event.ChangedAt));

        return (Results.NoContent(), outgoing);
    }
}
