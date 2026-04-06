using Marten;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Http;

namespace Promotions.Promotion;

public static class ActivatePromotionHandler
{
    public static async Task<Promotion?> LoadAsync(
        ActivatePromotion command,
        IQuerySession session,
        CancellationToken ct)
    {
        return await session.Events.AggregateStreamAsync<Promotion>(command.PromotionId, token: ct);
    }

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

    public static async Task Handle(
        ActivatePromotion command,
        Promotion promotion,
        IDocumentSession session,
        CancellationToken ct)
    {
        // FetchForWriting provides optimistic concurrency
        await session.Events.FetchForWriting<Promotion>(command.PromotionId, ct);

        var evt = new PromotionActivated(
            PromotionId: command.PromotionId,
            ActivatedAt: DateTimeOffset.UtcNow);

        // Tag the event for DCB tag table population
        var wrapped = session.Events.BuildEvent(evt);
        wrapped.AddTag(new PromotionStreamId(command.PromotionId));
        session.Events.Append(command.PromotionId, wrapped);
    }
}
