using Marten.Events.Aggregation;
using Marten.Events.Projections;

namespace Promotions.Coupon;

/// <summary>
/// Inline Marten projection for CouponLookupView using MultiStreamProjection.
/// Lifecycle: ProjectionLifecycle.Inline (zero lag, same transaction as command).
/// Maps: Guid event streams → string-keyed documents (coupon code as document ID).
/// MultiStreamProjection allows aggregating events by CouponCode property.
/// </summary>
public sealed class CouponLookupViewProjection : MultiStreamProjection<CouponLookupView, string>
{
    public CouponLookupViewProjection()
    {
        // Tell Marten which property to use as the document ID for each event type
        // This allows Guid streams to produce string-keyed documents
        Identity<CouponIssued>(x => x.CouponCode);
        Identity<CouponRedeemed>(x => x.CouponCode);
        Identity<CouponRevoked>(x => x.CouponCode);
        Identity<CouponExpired>(x => x.CouponCode);
    }

    // Create method for CouponIssued (first event creates the document)
    public CouponLookupView Create(CouponIssued evt)
    {
        return new CouponLookupView
        {
            Id = evt.CouponCode.ToUpperInvariant(),
            Code = evt.CouponCode.ToUpperInvariant(),
            PromotionId = evt.PromotionId,
            Status = CouponStatus.Issued,
            IssuedAt = evt.IssuedAt
        };
    }

    public static CouponLookupView Apply(CouponLookupView view, CouponRedeemed evt)
    {
        return view with
        {
            Status = CouponStatus.Redeemed,
            RedeemedAt = evt.RedeemedAt,
            OrderId = evt.OrderId
        };
    }

    public static CouponLookupView Apply(CouponLookupView view, CouponRevoked evt)
    {
        return view with
        {
            Status = CouponStatus.Revoked
        };
    }

    public static CouponLookupView Apply(CouponLookupView view, CouponExpired evt)
    {
        return view with
        {
            Status = CouponStatus.Expired
        };
    }
}
