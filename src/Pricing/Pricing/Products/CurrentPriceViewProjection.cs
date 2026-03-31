using Marten.Events.Projections;

namespace Pricing.Products;

/// <summary>
/// Inline Marten projection for CurrentPriceView using MultiStreamProjection.
/// Lifecycle: ProjectionLifecycle.Inline (zero lag, same transaction as command).
/// Maps: Guid event streams → string-keyed documents (SKU as document ID).
/// MultiStreamProjection allows aggregating events by a property (SKU) different from stream ID.
/// </summary>
public sealed class CurrentPriceViewProjection : MultiStreamProjection<CurrentPriceView, string>
{
    public CurrentPriceViewProjection()
    {
        // Tell Marten which property to use as the document ID for each event type
        // This allows Guid streams to produce string-keyed documents
        Identity<InitialPriceSet>(x => x.Sku);
        Identity<PriceChanged>(x => x.Sku);
        Identity<PriceChangeScheduled>(x => x.Sku);
        Identity<ScheduledPriceChangeCancelled>(x => x.Sku);
        Identity<ScheduledPriceActivated>(x => x.Sku);
        Identity<FloorPriceSet>(x => x.Sku);
        Identity<CeilingPriceSet>(x => x.Sku);
        Identity<PriceCorrected>(x => x.Sku);
        Identity<PriceDiscontinued>(x => x.Sku);
    }

    // Create method for InitialPriceSet (first event creates the document)
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

    public static CurrentPriceView Apply(CurrentPriceView view, PriceChanged evt)
    {
        return view with
        {
            BasePrice = evt.NewPrice.Amount,
            Currency = evt.NewPrice.Currency,
            PreviousBasePrice = evt.OldPrice.Amount,
            PreviousPriceSetAt = evt.PreviousPriceSetAt,
            LastUpdatedAt = evt.ChangedAt
        };
    }

    public static CurrentPriceView Apply(CurrentPriceView view, PriceChangeScheduled evt)
    {
        return view with
        {
            HasPendingSchedule = true,
            ScheduledChangeAt = evt.ScheduledFor,
            ScheduledPrice = evt.ScheduledPrice.Amount,
            LastUpdatedAt = evt.ScheduledAt
        };
    }

    public static CurrentPriceView Apply(CurrentPriceView view, ScheduledPriceChangeCancelled evt)
    {
        return view with
        {
            HasPendingSchedule = false,
            ScheduledChangeAt = null,
            ScheduledPrice = null,
            LastUpdatedAt = evt.CancelledAt
        };
    }

    public static CurrentPriceView Apply(CurrentPriceView view, ScheduledPriceActivated evt)
    {
        return view with
        {
            BasePrice = evt.ActivatedPrice.Amount,
            PreviousBasePrice = view.BasePrice,
            PreviousPriceSetAt = view.LastUpdatedAt,
            HasPendingSchedule = false,
            ScheduledChangeAt = null,
            ScheduledPrice = null,
            LastUpdatedAt = evt.ActivatedAt
        };
    }

    public static CurrentPriceView Apply(CurrentPriceView view, FloorPriceSet evt)
    {
        return view with
        {
            FloorPrice = evt.FloorPrice.Amount,
            LastUpdatedAt = evt.SetAt
        };
    }

    public static CurrentPriceView Apply(CurrentPriceView view, CeilingPriceSet evt)
    {
        return view with
        {
            CeilingPrice = evt.CeilingPrice.Amount,
            LastUpdatedAt = evt.SetAt
        };
    }

    public static CurrentPriceView Apply(CurrentPriceView view, PriceCorrected evt)
    {
        return view with
        {
            BasePrice = evt.CorrectedPrice.Amount,
            PreviousBasePrice = evt.PreviousPrice.Amount,
            LastUpdatedAt = evt.CorrectedAt
        };
    }

    public static CurrentPriceView Apply(CurrentPriceView view, PriceDiscontinued evt)
    {
        return view with
        {
            Status = PriceStatus.Discontinued,
            HasPendingSchedule = false,
            ScheduledChangeAt = null,
            ScheduledPrice = null,
            LastUpdatedAt = evt.DiscontinuedAt
        };
    }
}
