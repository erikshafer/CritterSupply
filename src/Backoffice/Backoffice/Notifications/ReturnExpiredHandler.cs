using Marten;
using Messages.Contracts.Returns;

namespace Backoffice.Notifications;

/// <summary>
/// Integration message handler for ReturnExpired events from Returns BC.
/// Appends message to Backoffice event store for AlertFeedView projection processing.
/// </summary>
public static class ReturnExpiredHandler
{
    public static void Handle(ReturnExpired message, IDocumentSession session)
    {
        // Append integration message as event to Backoffice event store
        // AlertFeedView projection will process inline
        session.Events.Append(Guid.NewGuid(), message);
    }
}
