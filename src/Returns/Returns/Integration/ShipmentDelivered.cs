using Marten;
using Returns.ReturnProcessing;

namespace Returns.Integration;

/// <summary>
/// Handles Fulfillment.ShipmentDelivered integration event.
/// Creates a ReturnEligibilityWindow document that enables return requests for this order.
/// This is the foundation of the Returns BC — without delivery confirmation, no returns are possible.
/// </summary>
public static class ShipmentDeliveredHandler
{
    public static async Task Handle(
        Messages.Contracts.Fulfillment.ShipmentDelivered message,
        IDocumentSession session,
        CancellationToken ct)
    {
        // Idempotency: check if eligibility window already exists
        var existing = await session.LoadAsync<ReturnEligibilityWindow>(message.OrderId, ct);
        if (existing is not null)
            return;

        var deliveredAt = message.DeliveredAt;
        var windowExpiresAt = deliveredAt.AddDays(ReturnEligibilityWindow.ReturnWindowDays);

        var eligibilityWindow = new ReturnEligibilityWindow
        {
            Id = message.OrderId,
            OrderId = message.OrderId,
            CustomerId = Guid.Empty, // Enriched via future Orders query or event data
            DeliveredAt = deliveredAt,
            WindowExpiresAt = windowExpiresAt,
            EligibleItems = [] // Phase 1: Items validated at request time from command data
        };

        session.Store(eligibilityWindow);
    }
}
