using Marten;
using Messages.Contracts.Returns;

namespace Backoffice.Notifications;

/// <summary>
/// Integration message handler for ReturnRequested events from Returns BC.
/// Appends message to Backoffice event store for ReturnMetricsView projection processing.
/// </summary>
public static class ReturnRequestedHandler
{
    public static void Handle(ReturnRequested message, IDocumentSession session)
    {
        // Append integration message as event to Backoffice event store
        // ReturnMetricsView projection will process inline
        session.Events.Append(Guid.NewGuid(), message);
    }
}
