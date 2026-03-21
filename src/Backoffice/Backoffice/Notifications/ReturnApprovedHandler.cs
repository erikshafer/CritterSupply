using Marten;
using Messages.Contracts.Returns;

namespace Backoffice.Notifications;

/// <summary>
/// Integration message handler for ReturnApproved events from Returns BC.
/// Appends message to Backoffice event store for ReturnMetricsView projection processing.
/// </summary>
public static class ReturnApprovedHandler
{
    public static void Handle(ReturnApproved message, IDocumentSession session)
    {
        // Append integration message as event to Backoffice event store
        // ReturnMetricsView projection will process inline
        session.Events.Append(Guid.NewGuid(), message);
    }
}
