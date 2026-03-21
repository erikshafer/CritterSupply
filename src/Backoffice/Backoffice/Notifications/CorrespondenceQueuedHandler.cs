using Marten;
using Messages.Contracts.Correspondence;

namespace Backoffice.Notifications;

/// <summary>
/// Handler for CorrespondenceQueued integration message from Correspondence BC.
/// Appends message to Backoffice event store for CorrespondenceMetricsView projection.
/// </summary>
public static class CorrespondenceQueuedHandler
{
    public static void Handle(CorrespondenceQueued message, IDocumentSession session)
    {
        session.Events.Append(Guid.NewGuid(), message);
    }
}
