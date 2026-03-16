using Marten;
using Messages.Contracts.Payments;

namespace Backoffice.Notifications;

/// <summary>
/// Integration message handler for PaymentCaptured events from Payments BC.
/// Updates AdminDailyMetrics projection via Marten inline projection.
/// </summary>
public static class PaymentCapturedHandler
{
    /// <summary>
    /// Handle PaymentCaptured: Wolverine will append to event store, triggering inline projection update.
    /// </summary>
    public static void Handle(PaymentCaptured message, IDocumentSession session)
    {
        // Append the integration message as an event to Marten's event store
        // The AdminDailyMetricsProjection will automatically process it (inline lifecycle)
        session.Events.Append(Guid.NewGuid(), message);
    }
}
