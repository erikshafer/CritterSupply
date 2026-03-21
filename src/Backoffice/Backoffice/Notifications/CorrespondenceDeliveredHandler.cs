using Marten;
using Messages.Contracts.Correspondence;

namespace Backoffice.Notifications;

/// <summary>
/// Handler for CorrespondenceDelivered integration message from Correspondence BC.
/// Appends message to Backoffice event store for CorrespondenceMetricsView projection.
/// </summary>
public static class CorrespondenceDeliveredHandler
{
    public static void Handle(CorrespondenceDelivered message, IDocumentSession session)
    {
        session.Events.Append(Guid.NewGuid(), message);
    }
}
