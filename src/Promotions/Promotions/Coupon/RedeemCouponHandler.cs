using JasperFx.Events.Tags;
using Marten;
using Microsoft.AspNetCore.Mvc;
using Promotions.Promotion;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;

namespace Promotions.Coupon;

/// <summary>
/// DCB handler for coupon redemption.
/// Uses EventTagQuery to load events from both the Coupon stream and the Promotion stream,
/// projecting them into CouponRedemptionState for a single atomic consistency decision.
///
/// This replaces the previous single-stream Load/Before/Handle pattern.
/// Promotion usage count update is handled by choreography:
/// CouponRedeemed → RecordPromotionRedemptionHandler (reacts to event).
///
/// M40.0: CritterSupply's first DCB handler implementation.
/// </summary>
public static class RedeemCouponHandler
{
    /// <summary>
    /// Builds the EventTagQuery spanning Coupon + Promotion streams.
    /// Marten loads all matching events and projects them into CouponRedemptionState.
    /// </summary>
    public static EventTagQuery Load(RedeemCoupon cmd)
        => EventTagQuery
            .For(CouponStreamTag.FromCode(cmd.CouponCode))
            .AndEventsOfType<CouponIssued, CouponRedeemed, CouponRevoked, CouponExpired>()
            .Or(new PromotionTag(cmd.PromotionId))
            .AndEventsOfType<PromotionCreated, PromotionActivated, PromotionPaused, PromotionResumed>()
            .AndEventsOfType<PromotionCancelled, PromotionExpired, PromotionRedemptionRecorded>();

    /// <summary>
    /// Validates the boundary state before handling.
    /// Enforces: coupon existence, coupon status, promotion existence,
    /// promotion status, and usage cap — all in one decision.
    /// </summary>
    public static ProblemDetails Before(RedeemCoupon cmd, [BoundaryModel] CouponRedemptionState? state)
    {
        if (state is null || !state.CouponExists)
            return new ProblemDetails
            {
                Detail = $"Coupon with code '{cmd.CouponCode}' not found",
                Status = 404
            };

        if (state.CouponStatus != CouponStatus.Issued)
            return new ProblemDetails
            {
                Detail = $"Cannot redeem coupon '{cmd.CouponCode}' — current status is {state.CouponStatus}. " +
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
                Detail = $"Cannot redeem coupon — promotion '{cmd.PromotionId}' status is {state.PromotionStatus}. " +
                         "Only Active promotions accept redemptions.",
                Status = 409
            };

        if (state.UsageLimit.HasValue && state.CurrentRedemptionCount >= state.UsageLimit.Value)
            return new ProblemDetails
            {
                Detail = $"Promotion '{cmd.PromotionId}' usage limit of {state.UsageLimit.Value} " +
                         "has been reached.",
                Status = 409
            };

        return WolverineContinue.NoProblems;
    }

    /// <summary>
    /// Appends CouponRedeemed to the Coupon stream via IDocumentSession.
    /// Optimistic concurrency is enforced by the DCB tag query boundary
    /// (AssertDcbConsistency registered during FetchForWritingByTags in Load).
    ///
    /// Uses session.Events.Append() directly because IEventBoundary.AppendOne()
    /// requires event-type-based tag inference, which doesn't work for events
    /// with raw Guid properties (CritterSupply convention) instead of strong-typed
    /// tag IDs. The consistency check still fires at SaveChangesAsync() time.
    ///
    /// CouponRedeemed contains PromotionId, enabling choreography:
    /// RecordPromotionRedemptionHandler reacts to this event to update the Promotion's usage count.
    /// </summary>
    public static void Handle(
        RedeemCoupon cmd,
        [BoundaryModel] CouponRedemptionState state,
        IDocumentSession session)
    {
        var evt = new CouponRedeemed(
            state.CouponId,
            state.CouponCode,
            cmd.PromotionId,
            cmd.OrderId,
            cmd.CustomerId,
            cmd.RedeemedAt);

        session.Events.Append(Coupon.StreamId(cmd.CouponCode), evt);
    }
}
