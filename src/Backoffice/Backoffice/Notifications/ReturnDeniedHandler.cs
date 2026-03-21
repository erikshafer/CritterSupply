using Marten;
using Messages.Contracts.Returns;

namespace Backoffice.Notifications;

/// <summary>
/// Integration message handler for ReturnDenied events from Returns BC.
/// Appends message to Backoffice event store for ReturnMetricsView projection processing.
/// </summary>
public static class ReturnDeniedHandler
{
    public static void Handle(ReturnDenied message, IDocumentSession session)
    {
        // Append integration message as event to Backoffice event store
        // ReturnMetricsView projection will process inline
        session.Events.Append(Guid.NewGuid(), message);
    }
}
