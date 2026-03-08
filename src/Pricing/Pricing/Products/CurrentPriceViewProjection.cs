using Marten.Events.Aggregation;

namespace Pricing.Products;

/// <summary>
/// Inline Marten projection for CurrentPriceView.
/// Lifecycle: ProjectionLifecycle.Inline (zero lag, same transaction as command).
/// Listens to: ProductPrice event stream.
/// Document ID: SKU string (normalized to uppercase).
/// </summary>
public sealed class CurrentPriceViewProjection : SingleStreamProjection<CurrentPriceView, string>
{
    // Marten will automatically use the Sku property from events as the document ID
    // This enables direct lookup: session.LoadAsync<CurrentPriceView>("DOG-FOOD-5LB")

    // ========== Apply Methods ==========
    // Pure functions: (CurrentView, Event) → NewView
    // Denormalize Money value objects to decimal + string Currency

    public CurrentPriceView Create(InitialPriceSet evt)
    {
        return new CurrentPriceView
        {
            Id = evt.Sku,
            Sku = evt.Sku,
            BasePrice = evt.Price.Amount,
            Currency = evt.Price.Currency,
            FloorPrice = evt.FloorPrice?.Amount,
            CeilingPrice = evt.CeilingPrice?.Amount,
            Status = PriceStatus.Published,
            HasPendingSchedule = false,
            LastUpdatedAt = evt.PricedAt
        };
    }

    public CurrentPriceView Apply(PriceChanged evt, CurrentPriceView current)
    {
        return current with
        {
            BasePrice = evt.NewPrice.Amount,
            Currency = evt.NewPrice.Currency,
            PreviousBasePrice = evt.OldPrice.Amount,
            PreviousPriceSetAt = evt.PreviousPriceSetAt,
            LastUpdatedAt = evt.ChangedAt
        };
    }

    public CurrentPriceView Apply(PriceChangeScheduled evt, CurrentPriceView current)
    {
        return current with
        {
            HasPendingSchedule = true,
            ScheduledChangeAt = evt.ScheduledFor,
            ScheduledPrice = evt.ScheduledPrice.Amount,
            LastUpdatedAt = evt.ScheduledAt
        };
    }

    public CurrentPriceView Apply(ScheduledPriceChangeCancelled evt, CurrentPriceView current)
    {
        return current with
        {
            HasPendingSchedule = false,
            ScheduledChangeAt = null,
            ScheduledPrice = null,
            LastUpdatedAt = evt.CancelledAt
        };
    }

    public CurrentPriceView Apply(ScheduledPriceActivated evt, CurrentPriceView current)
    {
        return current with
        {
            BasePrice = evt.ActivatedPrice.Amount,
            PreviousBasePrice = current.BasePrice,
            PreviousPriceSetAt = current.LastUpdatedAt,
            HasPendingSchedule = false,
            ScheduledChangeAt = null,
            ScheduledPrice = null,
            LastUpdatedAt = evt.ActivatedAt
        };
    }

    public CurrentPriceView Apply(FloorPriceSet evt, CurrentPriceView current)
    {
        return current with
        {
            FloorPrice = evt.FloorPrice.Amount,
            LastUpdatedAt = evt.SetAt
        };
    }

    public CurrentPriceView Apply(CeilingPriceSet evt, CurrentPriceView current)
    {
        return current with
        {
            CeilingPrice = evt.CeilingPrice.Amount,
            LastUpdatedAt = evt.SetAt
        };
    }

    public CurrentPriceView Apply(PriceCorrected evt, CurrentPriceView current)
    {
        return current with
        {
            BasePrice = evt.CorrectedPrice.Amount,
            PreviousBasePrice = evt.PreviousPrice.Amount,
            LastUpdatedAt = evt.CorrectedAt
        };
    }

    public CurrentPriceView Apply(PriceDiscontinued evt, CurrentPriceView current)
    {
        return current with
        {
            Status = PriceStatus.Discontinued,
            HasPendingSchedule = false,
            ScheduledChangeAt = null,
            ScheduledPrice = null,
            LastUpdatedAt = evt.DiscontinuedAt
        };
    }
}
