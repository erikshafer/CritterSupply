using Marten;

namespace Promotions.Promotion;

public static class CreatePromotionHandler
{
    public static void Handle(CreatePromotion command, IDocumentSession session)
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

        // Tag the event for DCB tag table population
        var wrapped = session.Events.BuildEvent(evt);
        wrapped.AddTag(new PromotionStreamId(promotionId));
        session.Events.StartStream<Promotions.Promotion.Promotion>(promotionId, wrapped);
    }
}
