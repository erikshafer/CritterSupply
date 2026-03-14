using Marten;
using Wolverine;
using Wolverine.Marten;

namespace Promotions.Coupon;

public static class IssueCouponHandler
{
    public static async Task<IStartStream> Handle(
        IssueCoupon command,
        IDocumentSession session,
        CancellationToken ct)
    {
        // Verify the parent promotion exists and is active
        var promotion = await session.Events.AggregateStreamAsync<Promotions.Promotion.Promotion>(
            command.PromotionId,
            token: ct);

        if (promotion is null)
        {
            throw new InvalidOperationException(
                $"Promotion {command.PromotionId} not found");
        }

        if (promotion.Status != PromotionStatus.Active)
        {
            throw new InvalidOperationException(
                $"Cannot issue coupon for promotion in {promotion.Status} status. " +
                $"Promotion must be Active.");
        }

        // Check if coupon code already exists (idempotency)
        var streamId = Promotions.Coupon.Coupon.StreamId(command.CouponCode);
        var existingCoupon = await session.Events.AggregateStreamAsync<Promotions.Coupon.Coupon>(
            streamId,
            token: ct);

        if (existingCoupon is not null)
        {
            throw new InvalidOperationException(
                $"Coupon with code '{command.CouponCode}' already exists");
        }

        var now = DateTimeOffset.UtcNow;

        var evt = new CouponIssued(
            CouponCode: command.CouponCode,
            PromotionId: command.PromotionId,
            IssuedAt: now);

        // Start new event stream for the coupon
        return MartenOps.StartStream<Promotions.Coupon.Coupon>(streamId, evt);
    }
}
