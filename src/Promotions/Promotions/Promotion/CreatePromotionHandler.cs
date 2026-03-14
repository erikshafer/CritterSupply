using Marten;
using Wolverine;

namespace Promotions.Promotion;

public static class CreatePromotionHandler
{
    public static (Promotion, PromotionCreated) Handle(CreatePromotion command)
    {
        // Phase 1: Using Guid.CreateVersion7() for time-ordered IDs
        var promotionId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;

        var promotion = Promotions.Promotion.Promotion.Create(
            id: promotionId,
            name: command.Name,
            description: command.Description,
            discountType: command.DiscountType,
            discountValue: command.DiscountValue,
            startDate: command.StartDate,
            endDate: command.EndDate,
            usageLimit: command.UsageLimit,
            createdAt: now);

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

        return (promotion, evt);
    }
}
