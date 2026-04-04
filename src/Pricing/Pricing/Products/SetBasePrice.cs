using FluentValidation;
using Marten;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Http;

namespace Pricing.Products;

/// <summary>
/// Command: Set base price for a product via HTTP endpoint.
/// Handles both Unpriced → Published (InitialPriceSet) and Published → Published (PriceChanged) paths.
/// </summary>
public sealed record SetBasePrice(string Sku, decimal Amount, string Currency = "USD");

public sealed class SetBasePriceValidator : AbstractValidator<SetBasePrice>
{
    public SetBasePriceValidator()
    {
        RuleFor(x => x.Amount)
            .GreaterThan(0)
            .WithMessage("Price amount must be greater than 0");

        RuleFor(x => x.Currency)
            .NotEmpty()
            .Length(3)
            .WithMessage("Currency must be a 3-letter ISO code (e.g., USD)");
    }
}

public static class SetBasePriceHandler
{
    /// <summary>
    /// Load: compute deterministic UUID v5 stream ID from SKU and fetch aggregate.
    /// Uses IQuerySession (read-only) — write happens in Handle via IDocumentSession.
    /// </summary>
    public static async Task<ProductPrice?> LoadAsync(
        string sku,
        IQuerySession session,
        CancellationToken ct)
    {
        var streamId = ProductPrice.StreamId(sku);
        return await session.Events.AggregateStreamAsync<ProductPrice>(streamId, token: ct);
    }

    /// <summary>
    /// Before: guard preconditions. Runs before Handle; short-circuits on ProblemDetails with Status != 200.
    /// Floor/ceiling validation here because Before receives the loaded aggregate.
    /// </summary>
    public static ProblemDetails Before(SetBasePrice cmd, ProductPrice? price)
    {
        if (price is null)
            return new ProblemDetails
            {
                Detail = $"Product '{cmd.Sku}' not found in Pricing BC",
                Status = 404
            };

        if (price.Status == PriceStatus.Discontinued)
            return new ProblemDetails
            {
                Detail = "Cannot set price for discontinued product",
                Status = 400
            };

        // For Published products, validate floor/ceiling constraints before Handle()
        if (price.Status == PriceStatus.Published && price.BasePrice is not null)
        {
            var newPrice = Money.Of(cmd.Amount, cmd.Currency);

            if (price.FloorPrice is not null && newPrice < price.FloorPrice)
                return new ProblemDetails
                {
                    Detail = $"New price {newPrice} is below floor price {price.FloorPrice}",
                    Status = 400
                };

            if (price.CeilingPrice is not null && newPrice > price.CeilingPrice)
                return new ProblemDetails
                {
                    Detail = $"New price {newPrice} exceeds ceiling price {price.CeilingPrice}",
                    Status = 400
                };
        }

        return WolverineContinue.NoProblems;
    }

    [WolverinePost("/api/pricing/products/{sku}/base-price")]
    [Authorize(Policy = "PricingManager")]
    public static (IResult, OutgoingMessages) Handle(
        string sku,
        SetBasePrice cmd,
        ProductPrice price, // non-null guaranteed by Before()
        IDocumentSession session)
    {
        var streamId = ProductPrice.StreamId(sku);
        var now = DateTimeOffset.UtcNow;
        var outgoing = new OutgoingMessages();

        if (price.Status == PriceStatus.Unpriced)
        {
            var evt = new InitialPriceSet(
                ProductPriceId: streamId,
                Sku: sku.ToUpperInvariant(),
                Price: Money.Of(cmd.Amount, cmd.Currency),
                FloorPrice: null,
                CeilingPrice: null,
                SetBy: Guid.Empty, // TODO: Extract from JWT claim
                PricedAt: now);

            session.Events.Append(streamId, evt);

            return (Results.Ok(new
            {
                sku = sku.ToUpperInvariant(),
                basePrice = new { amount = cmd.Amount, currency = cmd.Currency },
                status = "Published",
                message = "Initial price set successfully"
            }), outgoing);
        }

        // Published path — floor/ceiling already validated in Before()
        var newPrice = Money.Of(cmd.Amount, cmd.Currency);
        var changeEvt = new PriceChanged(
            ProductPriceId: streamId,
            Sku: sku.ToUpperInvariant(),
            OldPrice: price.BasePrice!,
            NewPrice: newPrice,
            PreviousPriceSetAt: price.LastChangedAt ?? price.RegisteredAt,
            Reason: "Base price updated by PricingManager",
            ChangedBy: Guid.Empty, // TODO: Extract from JWT claim
            ChangedAt: now,
            BulkPricingJobId: null,
            SourceSuggestionId: null);

        session.Events.Append(streamId, changeEvt);

        return (Results.Ok(new
        {
            sku = sku.ToUpperInvariant(),
            oldPrice = new { amount = price.BasePrice!.Amount, currency = price.BasePrice.Currency },
            newPrice = new { amount = newPrice.Amount, currency = newPrice.Currency },
            message = "Base price updated successfully"
        }), outgoing);
    }
}
