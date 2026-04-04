using FluentValidation;
using Marten;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Http;

namespace Pricing.Products;

/// <summary>
/// Command: Schedule a future price change for a Published product.
/// PricingManager role can schedule price changes that activate at a specified future date/time.
/// </summary>
public sealed record SchedulePriceChange(
    string Sku,
    decimal NewAmount,
    string Currency,
    DateTimeOffset ScheduledFor);

public sealed class SchedulePriceChangeValidator : AbstractValidator<SchedulePriceChange>
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

public static class SchedulePriceChangeHandler
{
    /// <summary>
    /// Load: compute deterministic UUID v5 stream ID from SKU and fetch aggregate.
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
    /// Before: guard preconditions including product existence, status, pending schedule, and floor/ceiling.
    /// </summary>
    public static ProblemDetails Before(SchedulePriceChange cmd, ProductPrice? price)
    {
        if (price is null)
            return new ProblemDetails
            {
                Detail = $"Product '{cmd.Sku}' not found in Pricing BC",
                Status = 404
            };

        if (price.Status != PriceStatus.Published)
            return new ProblemDetails
            {
                Detail = $"Cannot schedule price change for product in {price.Status} status. Product must be Published.",
                Status = 400
            };

        if (price.PendingSchedule is not null)
            return new ProblemDetails
            {
                Detail = "Product already has a pending scheduled price change. Cancel existing schedule first.",
                Status = 409,
                Extensions =
                {
                    ["existingSchedule"] = new
                    {
                        scheduleId = price.PendingSchedule.ScheduleId,
                        scheduledPrice = new { amount = price.PendingSchedule.ScheduledPrice.Amount, currency = price.PendingSchedule.ScheduledPrice.Currency },
                        scheduledFor = price.PendingSchedule.ScheduledFor
                    }
                }
            };

        var newPrice = Money.Of(cmd.NewAmount, cmd.Currency);

        if (price.FloorPrice is not null && newPrice < price.FloorPrice)
            return new ProblemDetails
            {
                Detail = $"Scheduled price {newPrice} is below floor price {price.FloorPrice}",
                Status = 400
            };

        if (price.CeilingPrice is not null && newPrice > price.CeilingPrice)
            return new ProblemDetails
            {
                Detail = $"Scheduled price {newPrice} exceeds ceiling price {price.CeilingPrice}",
                Status = 400
            };

        return WolverineContinue.NoProblems;
    }

    [WolverinePost("/api/pricing/products/{sku}/schedule")]
    [Authorize(Policy = "PricingManager")]
    public static async Task<(IResult, OutgoingMessages)> Handle(
        string sku,
        SchedulePriceChange cmd,
        ProductPrice price, // non-null guaranteed by Before()
        IDocumentSession session,
        IMessageBus messaging)
    {
        var streamId = ProductPrice.StreamId(sku);
        var scheduleId = Guid.NewGuid();
        var newPrice = Money.Of(cmd.NewAmount, cmd.Currency);

        var evt = new PriceChangeScheduled(
            ProductPriceId: streamId,
            Sku: sku.ToUpperInvariant(),
            ScheduleId: scheduleId,
            ScheduledPrice: newPrice,
            ScheduledFor: cmd.ScheduledFor,
            ScheduledBy: Guid.Empty, // TODO: Extract from JWT claim
            ScheduledAt: DateTimeOffset.UtcNow);

        session.Events.Append(streamId, evt);

        // ScheduleAsync cannot be expressed via OutgoingMessages — IMessageBus is the justified use here
        var activationMessage = new ActivateScheduledPriceChange(sku, scheduleId);
        await messaging.ScheduleAsync(activationMessage, cmd.ScheduledFor);

        var outgoing = new OutgoingMessages();
        return (Results.Ok(new
        {
            sku = sku.ToUpperInvariant(),
            scheduleId,
            scheduledPrice = new { amount = newPrice.Amount, currency = newPrice.Currency },
            scheduledFor = cmd.ScheduledFor,
            message = "Price change scheduled successfully"
        }), outgoing);
    }
}

/// <summary>
/// Internal command to activate a scheduled price change.
/// Sent as a delayed Wolverine message by SchedulePriceChangeHandler.
/// </summary>
public sealed record ActivateScheduledPriceChange(string Sku, Guid ScheduleId);

/// <summary>
/// Handler for the delayed ActivateScheduledPriceChange message.
/// Uses Load() pattern (same deterministic stream ID logic).
/// HandlerContinuation.Stop is correct here — this is not an HTTP handler;
/// there is no caller expecting a ProblemDetails response for stale/cancelled schedules.
/// </summary>
public static class ActivateScheduledPriceChangeHandler
{
    public static async Task<ProductPrice?> LoadAsync(
        ActivateScheduledPriceChange cmd,
        IQuerySession session,
        CancellationToken ct)
        => await session.Events.AggregateStreamAsync<ProductPrice>(
            ProductPrice.StreamId(cmd.Sku), token: ct);

    public static HandlerContinuation Before(
        ActivateScheduledPriceChange cmd,
        ProductPrice? price)
    {
        if (price is null)
            return HandlerContinuation.Stop; // product deleted after schedule was created

        if (price.PendingSchedule is null || price.PendingSchedule.ScheduleId != cmd.ScheduleId)
            return HandlerContinuation.Stop; // schedule cancelled or superseded — discard stale message

        return HandlerContinuation.Continue;
    }

    public static void Handle(
        ActivateScheduledPriceChange cmd,
        ProductPrice price, // non-null, schedule valid — guaranteed by Before()
        IDocumentSession session)
    {
        var streamId = ProductPrice.StreamId(cmd.Sku);
        var evt = new ScheduledPriceActivated(
            ProductPriceId: streamId,
            Sku: cmd.Sku.ToUpperInvariant(),
            ScheduleId: cmd.ScheduleId,
            ActivatedPrice: price.PendingSchedule!.ScheduledPrice,
            ActivatedAt: DateTimeOffset.UtcNow);

        session.Events.Append(streamId, evt);
    }
}
