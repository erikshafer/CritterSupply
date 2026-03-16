using Backoffice.Projections;
using Marten;

namespace Backoffice.Commands;

/// <summary>
/// Command to acknowledge an alert in the operations alert feed.
/// Updates AlertFeedView projection to mark alert as acknowledged.
/// Used by warehouse clerks and operations team to track alert handling.
/// </summary>
public sealed record AcknowledgeAlert(
    Guid AlertId,
    Guid AdminUserId);

/// <summary>
/// Handler for AcknowledgeAlert command.
/// Loads AlertFeedView projection document and updates acknowledgment fields.
/// </summary>
public static class AcknowledgeAlertHandler
{
    public static async Task Handle(
        AcknowledgeAlert cmd,
        IDocumentSession session,
        CancellationToken ct)
    {
        // Load existing alert from projection
        var alert = await session.LoadAsync<AlertFeedView>(cmd.AlertId, ct);

        if (alert is null)
            throw new InvalidOperationException($"Alert {cmd.AlertId} not found");

        if (alert.AcknowledgedBy is not null)
            throw new InvalidOperationException($"Alert {cmd.AlertId} already acknowledged by {alert.AcknowledgedBy}");

        // Update with acknowledgment fields (immutable update)
        var acknowledged = alert with
        {
            AcknowledgedBy = cmd.AdminUserId,
            AcknowledgedAt = DateTimeOffset.UtcNow
        };

        session.Store(acknowledged);
        await session.SaveChangesAsync(ct);
    }
}
