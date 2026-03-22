using Marten;
using Messages.Contracts.Correspondence;

namespace Backoffice.Notifications;

/// <summary>
/// Handler for CorrespondenceFailed integration message from Correspondence BC.
/// Appends message to Backoffice event store for CorrespondenceMetricsView projection.
/// </summary>
public static class CorrespondenceFailedHandler
{
    public static void Handle(CorrespondenceFailed message, IDocumentSession session)
    {
        session.Events.Append(Guid.NewGuid(), message);
    }
}
