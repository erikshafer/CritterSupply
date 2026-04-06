using JasperFx.Events.Tags;
using Marten;
using Marten.Events.Dcb;
using Microsoft.AspNetCore.Mvc;
using Promotions.Promotion;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;

namespace Promotions.Coupon;

/// <summary>
/// DCB handler for coupon redemption — real EventTagQuery + [BoundaryModel] + IEventBoundary&lt;T&gt;.
///
/// Load() returns EventTagQuery spanning both Coupon and Promotion streams.
/// Marten loads all tagged events and projects CouponRedemptionState via Apply() methods.
/// Before() validates the boundary state (no [BoundaryModel] — causes CS0128 if on both).
/// Handle() receives [BoundaryModel] IEventBoundary&lt;CouponRedemptionState&gt; for atomic append
/// with cross-stream optimistic concurrency via AssertDcbConsistency.
///
/// M40.0 S1B: Replaces the manual LoadAsync approach from S1 with Marten's native DCB API.
/// </summary>
public static class RedeemCouponHandler
{
    /// <summary>
    /// DCB load: EventTagQuery spanning both the Coupon and Promotion streams.
    /// Marten uses this to load all matching tagged events and project CouponRedemptionState.
    /// Each .For()/.Or() MUST be followed by .AndEventsOfType&lt;...&gt;() to create a condition.
    /// </summary>
    public static EventTagQuery Load(RedeemCoupon cmd)
        => EventTagQuery
            .For(new CouponStreamId(Coupon.StreamId(cmd.CouponCode)))
            .AndEventsOfType<CouponIssued, CouponRedeemed, CouponRevoked, CouponExpired>()
            .Or(new PromotionStreamId(cmd.PromotionId))
            .AndEventsOfType<PromotionCreated, PromotionActivated, PromotionPaused, PromotionResumed, PromotionCancelled, PromotionExpired>()
            .AndEventsOfType<PromotionRedemptionRecorded>();

    /// <summary>
    /// Validates all redemption invariants against the projected boundary state.
    /// Note: do NOT add [BoundaryModel] here — Wolverine passes the state from the
    /// boundary load automatically, and [BoundaryModel] on both Before and Handle
    /// causes CS0128 (duplicate local variable in generated code).
    /// </summary>
    public static ProblemDetails Before(RedeemCoupon cmd, CouponRedemptionState? state)
    {
        if (state is null || !state.CouponExists)
            return new ProblemDetails
            {
                Detail = $"Coupon '{cmd.CouponCode}' not found",
                Status = 404
            };

        if (state.CouponStatus != CouponStatus.Issued)
            return new ProblemDetails
            {
                Detail = $"Cannot redeem coupon '{cmd.CouponCode}' — status is {state.CouponStatus}. " +
                         "Only Issued coupons can be redeemed.",
                Status = 409
            };

        if (!state.PromotionExists)
            return new ProblemDetails
            {
                Detail = $"Promotion '{cmd.PromotionId}' not found",
                Status = 404
            };

        if (state.PromotionStatus != PromotionStatus.Active)
            return new ProblemDetails
            {
                Detail = $"Cannot redeem — promotion '{cmd.PromotionId}' status is {state.PromotionStatus}. " +
                         "Only Active promotions accept redemptions.",
                Status = 409
            };

        if (state.UsageLimit.HasValue && state.CurrentRedemptionCount >= state.UsageLimit.Value)
            return new ProblemDetails
            {
                Detail = $"Promotion '{cmd.PromotionId}' usage limit of {state.UsageLimit.Value} reached.",
                Status = 409
            };

        return WolverineContinue.NoProblems;
    }

    /// <summary>
    /// Appends CouponRedeemed via the DCB boundary.
    /// IEventBoundary provides cross-stream optimistic concurrency:
    /// AssertDcbConsistency runs at SaveChangesAsync and throws DcbConcurrencyException
    /// if any matching tagged event was appended since the boundary was loaded.
    /// </summary>
    public static OutgoingMessages Handle(
        RedeemCoupon cmd,
        [BoundaryModel] IEventBoundary<CouponRedemptionState> boundary,
        IDocumentSession session)
    {
        var state = boundary.Aggregate!;
        var couponStreamId = Coupon.StreamId(cmd.CouponCode);

        var evt = new CouponRedeemed(
            state.CouponId,
            state.CouponCode,
            cmd.PromotionId,
            cmd.OrderId,
            cmd.CustomerId,
            cmd.RedeemedAt);

        // Tag the event and append via boundary for DCB concurrency.
        // boundary.AppendOne() handles routing to the correct stream via tag registration.
        var wrapped = session.Events.BuildEvent(evt);
        wrapped.AddTag(new CouponStreamId(couponStreamId));
        boundary.AppendOne(wrapped);

        // Cascade CouponRedeemed for choreography → RecordPromotionRedemptionHandler
        var outgoing = new OutgoingMessages();
        outgoing.Add(evt);
        return outgoing;
    }
}
