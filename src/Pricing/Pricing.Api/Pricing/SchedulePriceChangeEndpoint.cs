using FluentValidation;
using Marten;
using Microsoft.AspNetCore.Authorization;
using Pricing.Products;
using Wolverine;
using Wolverine.Http;

namespace Pricing.Api.Pricing;

/// <summary>
/// HTTP endpoint: Schedule a future price change for a product.
/// PricingManager role can schedule price changes that will automatically activate at a specified date/time.
/// </summary>
public sealed record SchedulePriceChangeRequest(decimal NewAmount, string Currency, DateTimeOffset ScheduledFor);

public sealed class SchedulePriceChangeValidator : AbstractValidator<SchedulePriceChangeRequest>
{
    public SchedulePriceChangeValidator()
    {
        RuleFor(x => x.NewAmount)
            .GreaterThan(0)
            .WithMessage("Price amount must be greater than 0");

        RuleFor(x => x.Currency)
            .NotEmpty()
            .Length(3)
            .WithMessage("Currency must be a 3-letter ISO code (e.g., USD)");

        RuleFor(x => x.ScheduledFor)
            .GreaterThan(DateTimeOffset.UtcNow)
            .WithMessage("Scheduled date must be in the future");
    }
}

public static class SchedulePriceChangeEndpoint
{
    [WolverinePost("/api/pricing/products/{sku}/schedule")]
    [Authorize(Policy = "PricingManager")]
    public static async Task<IResult> SchedulePriceChange(
        string sku,
        SchedulePriceChangeRequest request,
        IDocumentSession session,
        IMessageContext messaging,
        CancellationToken ct)
    {
        var streamId = ProductPrice.StreamId(sku);
        var aggregate = await session.Events.AggregateStreamAsync<ProductPrice>(streamId, token: ct);

        if (aggregate is null)
        {
            return Results.NotFound(new { message = $"Product with SKU '{sku}' not found in Pricing BC." });
        }

        if (aggregate.Status != PriceStatus.Published)
        {
            return Results.BadRequest(new { message = $"Cannot schedule price change for product in {aggregate.Status} status. Product must be Published." });
        }

        if (aggregate.PendingSchedule is not null)
        {
            return Results.Conflict(new
            {
                message = "Product already has a pending scheduled price change. Cancel existing schedule first.",
                existingSchedule = new
                {
                    scheduleId = aggregate.PendingSchedule.ScheduleId,
                    scheduledPrice = new { amount = aggregate.PendingSchedule.ScheduledPrice.Amount, currency = aggregate.PendingSchedule.ScheduledPrice.Currency },
                    scheduledFor = aggregate.PendingSchedule.ScheduledFor
                }
            });
        }

        var newPrice = Money.Of(request.NewAmount, request.Currency);

        // Enforce floor/ceiling constraints if set
        if (aggregate.FloorPrice is not null && newPrice < aggregate.FloorPrice)
        {
            return Results.BadRequest(new { message = $"Scheduled price {newPrice} is below floor price {aggregate.FloorPrice}." });
        }

        if (aggregate.CeilingPrice is not null && newPrice > aggregate.CeilingPrice)
        {
            return Results.BadRequest(new { message = $"Scheduled price {newPrice} exceeds ceiling price {aggregate.CeilingPrice}." });
        }

        var scheduleId = Guid.NewGuid();
        var evt = new PriceChangeScheduled(
            ProductPriceId: streamId,
            Sku: sku.ToUpperInvariant(),
            ScheduleId: scheduleId,
            ScheduledPrice: newPrice,
            ScheduledFor: request.ScheduledFor,
            ScheduledBy: Guid.NewGuid(), // TODO: Get from JWT claim
            ScheduledAt: DateTimeOffset.UtcNow);

        session.Events.Append(streamId, evt);

        // Schedule Wolverine message to activate the price change at the specified time
        var activationMessage = new ActivateScheduledPriceChange(sku, scheduleId);
        await messaging.ScheduleAsync(activationMessage, request.ScheduledFor);

        await session.SaveChangesAsync(ct);

        return Results.Ok(new
        {
            sku = sku.ToUpperInvariant(),
            scheduleId,
            scheduledPrice = new { amount = newPrice.Amount, currency = newPrice.Currency },
            scheduledFor = request.ScheduledFor,
            message = "Price change scheduled successfully"
        });
    }
}

/// <summary>
/// Internal command to activate a scheduled price change.
/// Sent as a delayed Wolverine message by SchedulePriceChangeEndpoint.
/// </summary>
public sealed record ActivateScheduledPriceChange(string Sku, Guid ScheduleId);

public static class ActivateScheduledPriceChangeHandler
{
    public static async Task Handle(
        ActivateScheduledPriceChange command,
        IDocumentSession session,
        CancellationToken ct)
    {
        var streamId = ProductPrice.StreamId(command.Sku);
        var aggregate = await session.Events.AggregateStreamAsync<ProductPrice>(streamId, token: ct);

        if (aggregate is null)
        {
            // Product was deleted after schedule was created - discard message
            return;
        }

        if (aggregate.PendingSchedule is null || aggregate.PendingSchedule.ScheduleId != command.ScheduleId)
        {
            // Schedule was cancelled or superseded - discard stale message
            return;
        }

        // Activate the scheduled price change
        var evt = new ScheduledPriceActivated(
            ProductPriceId: streamId,
            Sku: command.Sku.ToUpperInvariant(),
            ScheduleId: command.ScheduleId,
            ActivatedPrice: aggregate.PendingSchedule.ScheduledPrice,
            ActivatedAt: DateTimeOffset.UtcNow);

        session.Events.Append(streamId, evt);
        await session.SaveChangesAsync(ct);
    }
}
