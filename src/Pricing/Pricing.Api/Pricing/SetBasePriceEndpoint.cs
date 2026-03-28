using FluentValidation;
using Marten;
using Microsoft.AspNetCore.Authorization;
using Pricing.Products;
using Wolverine.Http;
using Wolverine.Marten;

namespace Pricing.Api.Pricing;

/// <summary>
/// HTTP endpoint: Set base price for a product (works for both Unpriced and Published products).
/// PricingManager role can set initial prices or update existing prices.
/// </summary>
public sealed record SetBasePriceRequest(decimal Amount, string Currency = "USD");

public sealed class SetBasePriceValidator : AbstractValidator<SetBasePriceRequest>
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

public static class SetBasePriceEndpoint
{
    [WolverinePost("/api/pricing/products/{sku}/base-price")]
    [Authorize(Policy = "PricingManager")]
    public static async Task<IResult> SetBasePrice(
        string sku,
        SetBasePriceRequest request,
        IDocumentSession session,
        CancellationToken ct)
    {
        var streamId = ProductPrice.StreamId(sku);
        var aggregate = await session.Events.AggregateStreamAsync<ProductPrice>(streamId, token: ct);

        if (aggregate is null)
        {
            return Results.NotFound(new { message = $"Product with SKU '{sku}' not found in Pricing BC. Product must be registered first." });
        }

        // If product is Unpriced, use SetInitialPrice logic
        if (aggregate.Status == PriceStatus.Unpriced)
        {
            var initialPriceEvt = new InitialPriceSet(
                ProductPriceId: streamId,
                Sku: sku.ToUpperInvariant(),
                Price: Money.Of(request.Amount, request.Currency),
                FloorPrice: null, // No floor/ceiling for basic set
                CeilingPrice: null,
                SetBy: Guid.NewGuid(), // TODO: Get from JWT claim
                PricedAt: DateTimeOffset.UtcNow);

            session.Events.Append(streamId, initialPriceEvt);

            return Results.Ok(new
            {
                sku = sku.ToUpperInvariant(),
                basePrice = new { amount = request.Amount, currency = request.Currency },
                status = "Published",
                message = "Initial price set successfully"
            });
        }

        // If product is Published, use ChangePrice logic
        if (aggregate.Status == PriceStatus.Published)
        {
            if (aggregate.BasePrice is null)
            {
                return Results.BadRequest(new { message = "Product has no base price set. This should not happen for Published products." });
            }

            var newPrice = Money.Of(request.Amount, request.Currency);

            // Enforce floor/ceiling constraints if set
            if (aggregate.FloorPrice is not null && newPrice < aggregate.FloorPrice)
            {
                return Results.BadRequest(new { message = $"New price {newPrice} is below floor price {aggregate.FloorPrice}." });
            }

            if (aggregate.CeilingPrice is not null && newPrice > aggregate.CeilingPrice)
            {
                return Results.BadRequest(new { message = $"New price {newPrice} exceeds ceiling price {aggregate.CeilingPrice}." });
            }

            var priceChangedEvt = new PriceChanged(
                ProductPriceId: streamId,
                Sku: sku.ToUpperInvariant(),
                OldPrice: aggregate.BasePrice,
                NewPrice: newPrice,
                PreviousPriceSetAt: aggregate.LastChangedAt ?? aggregate.RegisteredAt,
                Reason: "Base price updated by PricingManager",
                ChangedBy: Guid.NewGuid(), // TODO: Get from JWT claim
                ChangedAt: DateTimeOffset.UtcNow,
                BulkPricingJobId: null,
                SourceSuggestionId: null);

            session.Events.Append(streamId, priceChangedEvt);

            return Results.Ok(new
            {
                sku = sku.ToUpperInvariant(),
                oldPrice = new { amount = aggregate.BasePrice.Amount, currency = aggregate.BasePrice.Currency },
                newPrice = new { amount = newPrice.Amount, currency = newPrice.Currency },
                message = "Base price updated successfully"
            });
        }

        // Product is Discontinued
        return Results.BadRequest(new { message = $"Cannot set price for product in {aggregate.Status} status." });
    }
}
