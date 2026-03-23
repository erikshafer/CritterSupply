using Marten;
using Messages.Contracts.ProductCatalog;
using VendorPortal.RealTime;

namespace VendorPortal.ChangeRequests;

/// <summary>
/// Handles <see cref="DescriptionChangeRejected"/> integration messages from the Catalog BC.
/// Transitions the change request to Rejected status with a reason, and emits SignalR hub messages.
/// If no reason is provided, defaults to "No reason provided".
/// </summary>
public static class DescriptionChangeRejectedHandler
{
    public static async Task<IEnumerable<object>> Handle(
        DescriptionChangeRejected @event,
        IDocumentSession session,
        CancellationToken ct)
    {
        var request = await session.LoadAsync<ChangeRequest>(@event.RequestId, ct);
        if (request is null || request.VendorTenantId != @event.VendorTenantId || !request.IsActive)
            return [];

        var reason = string.IsNullOrWhiteSpace(@event.Reason) ? "No reason provided" : @event.Reason;
        request.Status = ChangeRequestStatus.Rejected;
        request.RejectionReason = reason;
        request.ResolvedAt = @event.RejectedAt;
        session.Store(request);
        await session.SaveChangesAsync(ct);

        return
        [
            new ChangeRequestStatusUpdated(request.VendorTenantId, request.Id, request.Sku, "Rejected", @event.RejectedAt),
            new ChangeRequestDecisionPersonal(request.SubmittedByUserId, request.Id, request.Sku, "Rejected", reason, @event.RejectedAt, request.Type.ToString())
        ];
    }
}
