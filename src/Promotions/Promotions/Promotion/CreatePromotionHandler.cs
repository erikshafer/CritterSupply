using Wolverine;
using Wolverine.Marten;

namespace Promotions.Promotion;

public static class CreatePromotionHandler
{
    public static IStartStream Handle(CreatePromotion command)
    {
        // Phase 1: Using Guid.CreateVersion7() for time-ordered IDs
        var promotionId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;

        var evt = new PromotionCreated(
            PromotionId: promotionId,
            Name: command.Name,
            Description: command.Description,
            DiscountType: command.DiscountType,
            DiscountValue: command.DiscountValue,
            StartDate: command.StartDate,
            EndDate: command.EndDate,
            UsageLimit: command.UsageLimit,
            CreatedAt: now);

        // Start new event stream for the promotion
        return MartenOps.StartStream<Promotions.Promotion.Promotion>(promotionId, evt);
    }
}
