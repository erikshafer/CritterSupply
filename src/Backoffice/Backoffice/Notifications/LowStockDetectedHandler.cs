using Marten;
using Messages.Contracts.Inventory;

namespace Backoffice.Notifications;

/// <summary>
/// Integration message handler for LowStockDetected events from Inventory BC.
/// Appends message to Backoffice event store for AlertFeedView projection processing.
/// </summary>
public static class LowStockDetectedHandler
{
    public static void Handle(LowStockDetected message, IDocumentSession session)
    {
        // Append integration message as event to Backoffice event store
        // AlertFeedView projection will process inline
        session.Events.Append(Guid.NewGuid(), message);
    }
}
