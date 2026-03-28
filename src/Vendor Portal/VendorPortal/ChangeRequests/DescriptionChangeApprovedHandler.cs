using Marten;
using Messages.Contracts.ProductCatalog;
using VendorPortal.RealTime;

namespace VendorPortal.ChangeRequests;

/// <summary>
/// Handles <see cref="DescriptionChangeApproved"/> integration messages from the Catalog BC.
/// Transitions the change request to Approved status and emits SignalR hub messages to:
/// 1. All users in the vendor's group (tenant-wide update)
/// 2. The specific submitter user (personal notification)
/// </summary>
public static class DescriptionChangeApprovedHandler
{
    public static async Task<IEnumerable<object>> Handle(
        DescriptionChangeApproved @event,
        IDocumentSession session,
        CancellationToken ct)
    {
        var request = await session.LoadAsync<ChangeRequest>(@event.RequestId, ct);
        if (request is null || request.VendorTenantId != @event.VendorTenantId || !request.IsActive)
            return [];

        request.Status = ChangeRequestStatus.Approved;
        request.ResolvedAt = @event.ApprovedAt;
        session.Store(request);

        return
        [
            new ChangeRequestStatusUpdated(request.VendorTenantId, request.Id, request.Sku, "Approved", @event.ApprovedAt),
            new ChangeRequestDecisionPersonal(request.SubmittedByUserId, request.Id, request.Sku, "Approved", null, @event.ApprovedAt, request.Type.ToString())
        ];
    }
}
