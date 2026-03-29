using Marten;
using Messages.Contracts.ProductCatalog;
using VendorPortal.RealTime;

namespace VendorPortal.ChangeRequests;

/// <summary>
/// Handles <see cref="DataCorrectionApproved"/> integration messages from the Catalog BC.
/// Transitions the change request to Approved status and emits SignalR hub messages.
/// </summary>
public static class DataCorrectionApprovedHandler
{
    public static async Task<IEnumerable<object>> Handle(
        DataCorrectionApproved @event,
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
