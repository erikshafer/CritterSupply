using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;

namespace Promotions.Promotion;

public static class ActivatePromotionHandler
{
    public static ProblemDetails Before(
        ActivatePromotion command,
        Promotion? promotion)
    {
        if (promotion is null)
            return new ProblemDetails
            {
                Detail = $"Promotion '{command.PromotionId}' not found.",
                Status = 404
            };

        if (promotion.Status != PromotionStatus.Draft && promotion.Status != PromotionStatus.Paused)
            return new ProblemDetails
            {
                Detail = $"Cannot activate promotion in {promotion.Status} status. " +
                         $"Promotion must be in Draft or Paused status.",
                Status = 409
            };

        return WolverineContinue.NoProblems;
    }

    public static Events Handle(
        ActivatePromotion command,
        [WriteAggregate] Promotion promotion)
    {
        var events = new Events();
        events.Add(new PromotionActivated(
            PromotionId: command.PromotionId,
            ActivatedAt: DateTimeOffset.UtcNow));
        return events;
    }
}
