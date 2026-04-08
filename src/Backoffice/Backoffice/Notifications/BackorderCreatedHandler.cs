using Marten;
using Messages.Contracts.Fulfillment;

namespace Backoffice.Notifications;

/// <summary>
/// Handler for BackorderCreated integration message from Fulfillment BC.
/// Appends message to Backoffice event store for FulfillmentPipelineView projection.
/// Added in M41.0 S5 for pipeline exception visibility.
/// </summary>
public static class BackorderCreatedHandler
{
    public static void Handle(BackorderCreated message, IDocumentSession session)
    {
        session.Events.Append(Guid.NewGuid(), message);
    }
}
