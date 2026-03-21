using Marten;
using Messages.Contracts.Returns;

namespace Backoffice.Notifications;

/// <summary>
/// Integration message handler for ReturnReceived events from Returns BC.
/// Appends message to Backoffice event store for ReturnMetricsView projection processing.
/// </summary>
public static class ReturnReceivedHandler
{
    public static void Handle(ReturnReceived message, IDocumentSession session)
    {
        // Append integration message as event to Backoffice event store
        // ReturnMetricsView projection will process inline
        session.Events.Append(Guid.NewGuid(), message);
    }
}
