using FluentValidation;
using Marten;
using Microsoft.AspNetCore.Mvc;
using Wolverine.Http;

namespace Fulfillment.WorkOrders;

public sealed record ReleaseWave(
    Guid WorkOrderId,
    string WaveId)
{
    public sealed class Validator : AbstractValidator<ReleaseWave>
    {
        public Validator()
        {
            RuleFor(x => x.WorkOrderId).NotEmpty();
            RuleFor(x => x.WaveId).NotEmpty().MaximumLength(50);
        }
    }
}

/// <summary>
/// Handler for releasing a work order into a pick wave.
/// Appends WaveReleased and PickListCreated events.
/// </summary>
public static class ReleaseWaveHandler
{
    public static async Task<ProblemDetails> Before(
        ReleaseWave command,
        IDocumentSession session,
        CancellationToken ct)
    {
        var wo = await session.LoadAsync<WorkOrder>(command.WorkOrderId, ct);
        if (wo is null)
            return new ProblemDetails { Detail = "Work order not found", Status = 404 };

        if (wo.Status != WorkOrderStatus.Created)
            return new ProblemDetails
            {
                Detail = $"Cannot release wave for work order in {wo.Status} status",
                Status = 400
            };

        return WolverineContinue.NoProblems;
    }

    public static void Handle(ReleaseWave command, IDocumentSession session)
    {
        var now = DateTimeOffset.UtcNow;
        session.Events.Append(command.WorkOrderId,
            new WaveReleased(command.WaveId, now),
            new PickListCreated($"PL-{command.WaveId}-{command.WorkOrderId:N}", now));
    }
}
