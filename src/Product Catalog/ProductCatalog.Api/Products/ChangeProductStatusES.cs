using FluentValidation;
using Marten;
using Messages.Contracts.ProductCatalog;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProductCatalog.Products;
using Wolverine;
using Wolverine.Http;
using IntegrationProductStatusChanged = Messages.Contracts.ProductCatalog.ProductStatusChanged;

namespace ProductCatalog.Api.Products;

public sealed record ChangeProductStatusCommand(string Sku, ProductStatus NewStatus, string? Reason = null, bool IsRecall = false);

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
    public static async Task<(IResult, OutgoingMessages)> Handle(
        ChangeProductStatusCommand command,
        IDocumentSession session,
        CancellationToken ct)
    {
        var outgoing = new OutgoingMessages();

        var view = await session.Query<ProductCatalogView>()
            .Where(p => p.Sku == command.Sku)
            .FirstOrDefaultAsync(ct);

        if (view is null)
            return (Results.NotFound(new ProblemDetails { Detail = "Product not found", Status = 404 }), outgoing);

        if (view.IsDeleted)
            return (Results.Problem(detail: "Cannot change status of a deleted product", statusCode: 400), outgoing);

        var @event = new ProductCatalog.Products.ProductStatusChanged(
            ProductId: view.Id,
            PreviousStatus: view.Status,
            NewStatus: command.NewStatus,
            Reason: command.Reason,
            ChangedAt: DateTimeOffset.UtcNow);

        session.Events.Append(view.Id, @event);

        outgoing.Add(new IntegrationProductStatusChanged(
            Sku: command.Sku,
            PreviousStatus: view.Status.ToString(),
            NewStatus: command.NewStatus.ToString(),
            OccurredAt: @event.ChangedAt));

        if (command.NewStatus == ProductStatus.Discontinued)
        {
            outgoing.Add(new ProductDiscontinued(
                Sku: command.Sku,
                DiscontinuedAt: @event.ChangedAt,
                Reason: command.Reason,
                IsRecall: command.IsRecall));
        }

        return (Results.NoContent(), outgoing);
    }
}
