using Marten;

namespace Fulfillment.WorkOrders;

/// <summary>
/// Slice 29: SLA monitoring command.
/// Checks SLA thresholds on a specific WorkOrder.
/// </summary>
public sealed record CheckWorkOrderSLA(
    Guid WorkOrderId);

/// <summary>
/// Handler for checking SLA escalation thresholds on a WorkOrder.
/// Fires at configurable intervals. Appends SLAEscalationRaised at 50% and 75%,
/// and SLABreached at 100% of the SLA window.
/// </summary>
public static class CheckWorkOrderSLAHandler
{
    // Stub: standard SLA window is 4 hours
    private static readonly TimeSpan StandardSlaWindow = TimeSpan.FromHours(4);

    public static async Task Handle(
        CheckWorkOrderSLA command,
        IDocumentSession session,
        CancellationToken ct)
    {
        var wo = await session.LoadAsync<WorkOrder>(command.WorkOrderId, ct);
        if (wo is null) return;

        // Only check non-terminal statuses
        if (wo.Status is WorkOrderStatus.PackingCompleted or WorkOrderStatus.PickExceptionClosed)
            return;

        var elapsed = DateTimeOffset.UtcNow - wo.CreatedAt;
        var slaWindow = StandardSlaWindow;
        var percentElapsed = elapsed.TotalMilliseconds / slaWindow.TotalMilliseconds * 100;

        var now = DateTimeOffset.UtcNow;
        var eventsToAppend = new List<object>();

        // 50% threshold
        if (percentElapsed >= 50 && !wo.EscalationThresholdsMet.Contains(50))
        {
            eventsToAppend.Add(new SLAEscalationRaised(50, elapsed, slaWindow, now));
        }

        // 75% threshold
        if (percentElapsed >= 75 && !wo.EscalationThresholdsMet.Contains(75))
        {
            eventsToAppend.Add(new SLAEscalationRaised(75, elapsed, slaWindow, now));
        }

        // 100% threshold — SLA breach
        if (percentElapsed >= 100 && !wo.EscalationThresholdsMet.Contains(100))
        {
            eventsToAppend.Add(new SLABreached(elapsed, slaWindow, now));
        }

        if (eventsToAppend.Count > 0)
        {
            session.Events.Append(command.WorkOrderId, eventsToAppend.ToArray());
        }
    }
}
